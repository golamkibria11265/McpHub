using McpHub.Core.Errors;
using McpHub.Modules.Postgres;

namespace McpHub.UnitTests.Database;

public class PostgresGuardTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select * from orders where id = 5;")]
    [InlineData("WITH recent AS (SELECT id FROM orders) SELECT * FROM recent")]
    [InlineData("SELECT * FROM notes WHERE body = 'DROP TABLE x; -- not a comment'")]
    [InlineData("SELECT created_at, updated_by FROM t")]
    [InlineData("SELECT 'it''s fine; DELETE FROM x'")]
    public void Accepts_single_select_statements(string sql)
    {
        var scan = SqlGuard.EnsureReadOnly(sql);

        Assert.False(scan.Sql.EndsWith(';'));
    }

    [Theory]
    [InlineData("DELETE FROM orders")]
    [InlineData("UPDATE orders SET status = 'x'")]
    [InlineData("INSERT INTO orders VALUES (1)")]
    [InlineData("DROP TABLE orders")]
    [InlineData("TRUNCATE TABLE orders")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT 1; DROP TABLE orders")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT * INTO backup FROM orders")]
    [InlineData("SELECT 1 -- hide")]
    [InlineData("SELECT 1 /* hide */")]
    [InlineData("SELECT 1 # hide")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("WITH d AS (DELETE FROM orders RETURNING *) SELECT * FROM d")]
    [InlineData("SELECT * FROM orders FOR UPDATE")]
    [InlineData("SELECT * FROM orders FOR SHARE")]
    [InlineData("SELECT pg_sleep(30)")]
    [InlineData("SELECT pg_read_file('/etc/passwd')")]
    [InlineData("SELECT dblink('x','y')")]
    [InlineData("SELECT nextval('seq')")]
    [InlineData("SELECT $$x$$")]
    public void Rejects_anything_that_is_not_a_plain_select(string sql)
    {
        var ex = Assert.Throws<IntegrationException>(() => SqlGuard.EnsureReadOnly(sql));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
    }

    [Fact]
    public void Rejects_keywords_hidden_behind_case_changes()
    {
        Assert.Throws<IntegrationException>(() => SqlGuard.EnsureReadOnly("sElEcT 1; dRoP table x"));
    }

    [Fact]
    public void Quoted_identifiers_are_reported_so_restricted_columns_cannot_hide_in_quotes()
    {
        var scan = SqlGuard.EnsureReadOnly("SELECT \"card_number\" FROM payments");

        Assert.Contains("card_number", scan.Identifiers);
        Assert.Equal("card_number", new DeniedColumnPolicy().FindReference(scan.Identifiers));
    }

    [Fact]
    public void Literals_are_not_reported_as_identifiers()
    {
        var scan = SqlGuard.EnsureReadOnly("SELECT id FROM t WHERE note = 'cvv'");

        Assert.Null(new DeniedColumnPolicy().FindReference(scan.Identifiers));
    }
}
