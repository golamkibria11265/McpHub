using McpHub.Core.Errors;
using Microsoft.Extensions.Logging;

namespace McpHub.Modules.Postgres;

public sealed partial class PostgresDatabase
{
    /// <summary>
    /// Runs one INSERT/UPDATE (or DELETE) inside a transaction. With <paramref name="confirm"/> false the transaction is
    /// rolled back, so the caller sees how many rows WOULD change without anything changing.
    /// </summary>
    public async Task<WriteResult> ExecuteAsync(
        string? connection, string? sql, bool confirm, SqlStatementKind kind, CancellationToken cancellationToken)
    {
        var target = _registry.Resolve(connection);
        RequireWriteAllowed(target.Name, kind);

        var scan = SqlWriteGuard.Ensure(sql, kind);
        var restricted = _denied.FindReference(scan.Identifiers);
        if (restricted is not null)
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, Integration,
                $"The statement mentions the restricted column '{restricted}'. Restricted columns can never be written.");
        }

        await using var db = await OpenAsync(target, cancellationToken, writable: true);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = scan.Sql;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected > _options.MaxAffectedRows)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration,
                    $"The statement would change {affected} rows; the limit is {_options.MaxAffectedRows}. Nothing was changed. Narrow the WHERE clause.");
            }

            if (!confirm)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new WriteResult(target.Name, scan.Operation, affected, false,
                    $"Preview only: {affected} row(s) would change. Nothing was changed. Run again with confirm=true to apply.");
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Write on connection {Connection}: {Operation} changed {Rows} row(s)", target.Name, scan.Operation, affected);
            return new WriteResult(target.Name, scan.Operation, affected, true, $"{affected} row(s) changed and committed.");
        }
        catch (System.Data.Common.DbException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            _logger.LogInformation(ex, "Write failed on connection {Connection}", target.Name);
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration,
                $"The database rejected the statement: {ValueFormatter.Truncate(FirstLine(ex.Message), 300)}", ex);
        }
        catch (IntegrationException)
        {
            throw;
        }
        catch
        {
            await TryRollbackAsync(transaction);
            throw;
        }
    }

    private void RequireWriteAllowed(string connectionName, SqlStatementKind kind)
    {
        var settings = _options.Connections.First(c => string.Equals(c.Key, connectionName, StringComparison.OrdinalIgnoreCase)).Value;
        if (!settings.AllowWrite)
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, Integration, "Writes are not enabled for this connection.");
        }

        if (kind == SqlStatementKind.Delete && !settings.AllowDelete)
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, Integration, "Deletes are not enabled for this connection.");
        }
    }

    private static async Task TryRollbackAsync(System.Data.Common.DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            // The transaction is already finished or the connection is gone: nothing was committed either way.
        }
    }
}
