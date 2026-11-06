using McpHub.Core.Errors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PgGuard = McpHub.Modules.Postgres.SqlWriteGuard;
using PgKind = McpHub.Modules.Postgres.SqlStatementKind;
using SqlGuardW = McpHub.Modules.Sql.SqlWriteGuard;
using SqlKind = McpHub.Modules.Sql.SqlStatementKind;

namespace McpHub.UnitTests.Database;

public sealed class SqlWriteGuardTests
{
    [Theory]
    [InlineData("INSERT INTO dbo.Notes (Title) VALUES ('hi; there')")]
    [InlineData("UPDATE dbo.Notes SET Title = 'x' WHERE Id = 5;")]
    [InlineData("insert into Notes(Title) select Title from Old")]
    public void Accepts_plain_insert_and_update(string sql) =>
        Assert.NotNull(SqlGuardW.Ensure(sql, SqlKind.InsertOrUpdate).Sql);

    [Theory]
    [InlineData("UPDATE Notes SET Title = 'x'")]
    [InlineData("DROP TABLE Notes")]
    [InlineData("TRUNCATE TABLE Notes")]
    [InlineData("ALTER TABLE Notes ADD c int")]
    [InlineData("INSERT INTO Notes VALUES (1); DELETE FROM Notes WHERE 1=1")]
    [InlineData("INSERT INTO Notes VALUES (1) -- c")]
    [InlineData("UPDATE Notes SET a = 1 WHERE id = 1; EXEC xp_cmdshell 'dir'")]
    [InlineData("SELECT * FROM Notes")]
    [InlineData("DELETE FROM Notes WHERE id = 1")]
    [InlineData("UPDATE Notes SET a = 1 OUTPUT inserted.* WHERE id = 1")]
    [InlineData("INSERT INTO a SELECT * INTO b FROM c")]
    public void Rejects_everything_else_on_the_execute_tool(string sql) =>
        Assert.Throws<IntegrationException>(() => SqlGuardW.Ensure(sql, SqlKind.InsertOrUpdate));

    [Fact]
    public void Delete_tool_accepts_only_delete_with_where()
    {
        Assert.Equal("DELETE", SqlGuardW.Ensure("DELETE FROM Notes WHERE Id = 1", SqlKind.Delete).Operation);
        Assert.Throws<IntegrationException>(() => SqlGuardW.Ensure("DELETE FROM Notes", SqlKind.Delete));
        Assert.Throws<IntegrationException>(() => SqlGuardW.Ensure("UPDATE Notes SET a=1 WHERE id=1", SqlKind.Delete));
    }

    [Fact]
    public void Identifiers_are_reported_so_restricted_columns_can_be_refused()
    {
        var scan = SqlGuardW.Ensure("UPDATE Users SET [Password] = 'x' WHERE Id = 1", SqlKind.InsertOrUpdate);
        Assert.Contains("Password", scan.Identifiers);
    }

    [Theory]
    [InlineData("INSERT INTO notes (title) VALUES ('a')")]
    [InlineData("UPDATE notes SET title = 'a' WHERE id = 1")]
    public void Postgres_accepts_plain_writes(string sql) => Assert.NotNull(PgGuard.Ensure(sql, PgKind.InsertOrUpdate).Sql);

    [Theory]
    [InlineData("UPDATE notes SET a = 1 WHERE id = 1 RETURNING *")]
    [InlineData("INSERT INTO t VALUES (1) ON CONFLICT DO NOTHING")]
    [InlineData("UPDATE notes SET a = $$x$$ WHERE id = 1")]
    [InlineData("INSERT INTO t VALUES (pg_sleep(5))")]
    [InlineData("UPDATE notes SET a = 1")]
    public void Postgres_rejects_risky_writes(string sql) =>
        Assert.Throws<IntegrationException>(() => PgGuard.Ensure(sql, PgKind.InsertOrUpdate));
}

public sealed class SqlWriteGatingTests
{
    [Fact]
    public async Task Writes_are_refused_unless_enabled_for_the_connection()
    {
        var options = new McpHub.Modules.Sql.SqlOptions
        {
            Connections = { ["Main"] = new McpHub.Modules.Sql.SqlConnectionOptions() }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Modules:Sql:Credentials:Connections:Main:ConnectionString"] = "Server=[HOST]"
        }).Build();
        var db = new McpHub.Modules.Sql.SqlDatabase(Options.Create(options), config, NullLogger<McpHub.Modules.Sql.SqlDatabase>.Instance);

        var ex = await Assert.ThrowsAsync<IntegrationException>(() =>
            db.ExecuteAsync("Main", "INSERT INTO T VALUES (1)", true, SqlKind.InsertOrUpdate, default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);

        options.Connections["Main"].AllowWrite = true;
        var del = await Assert.ThrowsAsync<IntegrationException>(() =>
            db.ExecuteAsync("Main", "DELETE FROM T WHERE Id = 1", true, SqlKind.Delete, default));
        Assert.Equal(IntegrationErrorKind.Forbidden, del.Kind);
    }
}
