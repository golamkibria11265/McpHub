using System.Data.Common;
using System.Text;
using McpHub.Core.Errors;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Postgres;

/// <summary>Use cases for PostgreSQL: list tables, describe a table, run a read-only query.</summary>
public sealed partial class PostgresDatabase
{
    private const string Integration = PostgresModule.IntegrationName;

    private static readonly string[] SystemSchemas = ["information_schema", "pg_catalog", "pg_toast"];

    private readonly PostgresOptions _options;
    private readonly ConnectionRegistry _registry;
    private readonly DeniedColumnPolicy _denied;
    private readonly ILogger<PostgresDatabase> _logger;

    public PostgresDatabase(IOptions<PostgresOptions> options, IConfiguration configuration, ILogger<PostgresDatabase> logger)
    {
        _options = options.Value;
        _logger = logger;
        _registry = new ConnectionRegistry(_options, configuration);
        _denied = new DeniedColumnPolicy(_options.ExtraDeniedColumns);
    }

    public IReadOnlyList<ConnectionInfo> ListConnections() => _registry.List();

    public async Task<ObjectList> ListObjectsAsync(string? connection, string? schema, CancellationToken cancellationToken)
    {
        var target = _registry.Resolve(connection);
        var sql = new StringBuilder("SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE 1 = 1");
        var args = new List<KeyValuePair<string, string>>();
        AddSchemaFilter(sql, args, OptionalName(schema, "schema"));
        sql.Append(" ORDER BY TABLE_SCHEMA, TABLE_NAME");

        await using var db = await OpenAsync(target, cancellationToken);
        var (rows, truncated) = await ReadAsync(
            db, sql.ToString(), args, _options.MaxRows,
            reader => new ObjectInfo(Text(reader, 0), Text(reader, 1) ?? string.Empty, Text(reader, 2) ?? string.Empty),
            cancellationToken);

        return new ObjectList(target.Name, rows, truncated);
    }

    public async Task<ObjectDescription> DescribeAsync(string? connection, string? table, string? schema, CancellationToken cancellationToken)
    {
        var target = _registry.Resolve(connection);
        var tableName = RequireName(table, "name");
        var sql = new StringBuilder(
            "SELECT TABLE_SCHEMA, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH " +
            "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table");
        var args = new List<KeyValuePair<string, string>> { new("@table", tableName) };
        AddSchemaFilter(sql, args, OptionalName(schema, "schema"));
        sql.Append(" ORDER BY TABLE_SCHEMA, ORDINAL_POSITION");

        await using var db = await OpenAsync(target, cancellationToken);
        var (rows, _) = await ReadAsync(
            db, sql.ToString(), args, 1_000,
            reader => (Schema: Text(reader, 0), Column: Text(reader, 1) ?? string.Empty, Type: Text(reader, 2) ?? string.Empty,
                Nullable: string.Equals(Text(reader, 3), "YES", StringComparison.OrdinalIgnoreCase), Length: Length(reader, 4)),
            cancellationToken);

        if (rows.Count == 0)
        {
            throw new IntegrationException(IntegrationErrorKind.NotFound, Integration, $"Table or view '{tableName}' was not found.");
        }

        var schemas = rows.Select(r => r.Schema).Distinct(StringComparer.Ordinal).ToList();
        if (schemas.Count > 1)
        {
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration,
                $"'{tableName}' exists in several schemas ({string.Join(", ", schemas)}). Pass 'schema' to choose one.");
        }

        var columns = rows.Select(r => new ColumnInfo(r.Column, r.Type, r.Nullable, r.Length, _denied.IsDenied(r.Column))).ToList();
        return new ObjectDescription(target.Name, schemas[0], tableName, columns);
    }

    public async Task<QueryResult> QueryAsync(string? connection, string? sql, int? maxRows, CancellationToken cancellationToken)
    {
        var target = _registry.Resolve(connection);
        var scan = SqlGuard.EnsureReadOnly(sql);

        var restricted = _denied.FindReference(scan.Identifiers);
        if (restricted is not null)
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, Integration,
                $"The query mentions the restricted column '{restricted}'. Restricted columns are never returned.");
        }

        var limit = _options.ClampRows(maxRows);
        var cellLimit = _options.MaxCellChars;

        await using var db = await OpenAsync(target, cancellationToken);
        await using var command = db.CreateCommand();
        command.CommandText = scan.Sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var masked = columns.Select(_denied.IsDenied).ToList();
            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count == limit)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[columns.Count];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = masked[i] ? DeniedColumnPolicy.Mask : ValueFormatter.Format(reader.GetValue(i), cellLimit);
                }

                rows.Add(row);
            }

            if (truncated)
            {
                TryCancel(command);
            }

            var redacted = columns.Where((_, i) => masked[i]).ToList();
            return new QueryResult(target.Name, columns, rows, rows.Count, truncated, redacted);
        }
        catch (DbException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Query failed on connection {Connection}", target.Name);
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration,
                $"The database rejected the query: {ValueFormatter.Truncate(FirstLine(ex.Message), 300)}", ex);
        }
    }

    private async Task<DbConnection> OpenAsync(ResolvedConnection target, CancellationToken cancellationToken, bool writable = false)
    {
        DbConnection connection;
        try
        {
            connection = Create(target.ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            // Deliberately no inner exception: its message can quote parts of the connection string.
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration,
                $"The connection string configured for '{target.Name}' is invalid. Contact the administrator.");
        }

        try
        {
            await connection.OpenAsync(cancellationToken);
            if (!writable)
            {
                await PrepareReadOnlySessionAsync(connection, cancellationToken);
            }

            return connection;
        }
        catch (DbException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync();
            _logger.LogWarning(ex, "Could not open database connection {Connection}", target.Name);
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration, $"Could not connect to database '{target.Name}'.", ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<(List<T> Rows, bool Truncated)> ReadAsync<T>(
        DbConnection connection,
        string sql,
        List<KeyValuePair<string, string>> args,
        int maxRows,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;

        foreach (var (name, value) in args)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<T>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count == maxRows)
                {
                    truncated = true;
                    break;
                }

                rows.Add(map(reader));
            }

            if (truncated)
            {
                TryCancel(command);
            }

            return (rows, truncated);
        }
        catch (DbException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Catalog query failed");
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration, "The database could not be read.", ex);
        }
    }

    private void AddSchemaFilter(StringBuilder sql, List<KeyValuePair<string, string>> args, string? schema)
    {
        if (schema is not null)
        {
            sql.Append(" AND TABLE_SCHEMA = @schema");
            args.Add(new("@schema", schema));
            return;
        }

        var names = SystemSchemas.Select((s, i) => (Name: $"@sys{i}", Value: s)).ToList();
        sql.Append(" AND TABLE_SCHEMA NOT IN (").AppendJoin(", ", names.Select(n => n.Name)).Append(')');
        args.AddRange(names.Select(n => new KeyValuePair<string, string>(n.Name, n.Value)));
    }

    private string RequireName(string? value, string field) =>
        OptionalName(value, field) ?? throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration, $"'{field}' is required.");

    private string? OptionalName(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var name = value.Trim();
        if (name.Length > 128 || name.Any(char.IsControl))
        {
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration, $"'{field}' is not a valid name.");
        }

        return name;
    }

    private static DbConnection Create(string connectionString) => new NpgsqlConnection(connectionString);

    // Every transaction of this session is read-only: the server itself rejects writes, whatever the SQL says.
    private static async Task PrepareReadOnlySessionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? Text(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int? Length(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var length = Convert.ToInt64(reader.GetValue(ordinal));
        return length is > 0 and <= int.MaxValue ? (int)length : null;
    }

    private static string FirstLine(string message)
    {
        var index = message.IndexOfAny(['\r', '\n']);
        return index < 0 ? message : message[..index];
    }

    private static void TryCancel(DbCommand command)
    {
        try
        {
            command.Cancel();
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or NotSupportedException)
        {
            // Best effort: stops the server from streaming the rest of a big result we already decided to drop.
        }
    }
}
