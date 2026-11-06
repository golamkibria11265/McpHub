using McpHub.Core.Errors;
using McpHub.Modules.Mongo;
using McpHub.Modules.Postgres;
using McpHub.Modules.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoRegistry = McpHub.Modules.Mongo.ConnectionRegistry;
using PostgresRegistry = McpHub.Modules.Postgres.ConnectionRegistry;
using SqlRegistry = McpHub.Modules.Sql.ConnectionRegistry;

namespace McpHub.UnitTests.Database;

public class ModuleWiringTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static readonly Dictionary<string, string?> Valid = new()
    {
        ["Modules:Sql:Connections:Orders:Description"] = "Orders",
        ["Modules:Sql:Credentials:Connections:Orders:ConnectionString"] = "[SQLSERVER_CONNECTION_STRING]",
        ["Modules:Postgres:Connections:Reporting:Description"] = "Reporting",
        ["Modules:Postgres:Credentials:Connections:Reporting:ConnectionString"] = "[POSTGRES_CONNECTION_STRING]",
        ["Modules:Postgres:MaxRows"] = "10",
        ["Modules:Mongo:Connections:Events:Database"] = "events",
        ["Modules:Mongo:Credentials:Connections:Events:ConnectionString"] = "[MONGODB_CONNECTION_STRING]"
    };

    [Fact]
    public void Each_module_has_its_own_name_and_scope()
    {
        Assert.Equal("Sql", new SqlModule().Name);
        Assert.Equal("Postgres", new PostgresModule().Name);
        Assert.Equal("Mongo", new MongoModule().Name);
        Assert.Equal("sql.read", SqlScopes.Read);
        Assert.Equal("postgres.read", PostgresScopes.Read);
        Assert.Equal("mongo.read", MongoScopes.Read);
    }

    [Fact]
    public void Modules_can_be_enabled_together_without_sharing_settings()
    {
        var configuration = Config(Valid);
        var services = new ServiceCollection().AddLogging().AddSingleton(configuration);
        new SqlModule().ConfigureServices(services, configuration);
        new PostgresModule().ConfigureServices(services, configuration);
        new MongoModule().ConfigureServices(services, configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(200, provider.GetRequiredService<IOptions<SqlOptions>>().Value.MaxRows);
        Assert.Equal(10, provider.GetRequiredService<IOptions<PostgresOptions>>().Value.MaxRows);
        Assert.Equal("Orders", provider.GetRequiredService<SqlDatabase>().ListConnections()[0].Name);
        Assert.Equal("Reporting", provider.GetRequiredService<PostgresDatabase>().ListConnections()[0].Name);
        Assert.Equal("Events", provider.GetRequiredService<MongoService>().ListConnections()[0].Name);
        Assert.Equal("SqlServer", provider.GetRequiredService<SqlDatabase>().ListConnections()[0].Provider);
        Assert.Equal("PostgreSql", provider.GetRequiredService<PostgresDatabase>().ListConnections()[0].Provider);
    }

    [Fact]
    public void Startup_validation_fails_when_a_connection_string_is_missing()
    {
        var values = new Dictionary<string, string?>(Valid);
        values.Remove("Modules:Sql:Credentials:Connections:Orders:ConnectionString");
        var services = new ServiceCollection();
        new SqlModule().ConfigureServices(services, Config(values));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<SqlOptions>>().Value);

        Assert.Contains("ConnectionString", string.Join(" ", ex.Failures));
    }

    [Fact]
    public void Startup_validation_fails_when_no_connection_is_defined()
    {
        var services = new ServiceCollection();
        new PostgresModule().ConfigureServices(services, Config(new Dictionary<string, string?>()));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<PostgresOptions>>().Value);
    }

    [Fact]
    public void Registry_resolves_names_case_insensitively_and_hides_unknown_ones()
    {
        var options = new SqlOptions { Connections = { ["Orders"] = new SqlConnectionOptions() } };
        var registry = new SqlRegistry(options, Config(Valid));

        Assert.Equal("Orders", registry.Resolve("ORDERS").Name);
        var ex = Assert.Throws<IntegrationException>(() => registry.Resolve("secret-db"));
        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
        Assert.False(ex.Message.Contains("secret-db", StringComparison.Ordinal));
    }

    [Fact]
    public void Connection_strings_never_appear_in_any_connection_list()
    {
        var configuration = Config(Valid);
        var sql = new SqlRegistry(new SqlOptions { Connections = { ["Orders"] = new SqlConnectionOptions { Description = "Orders" } } }, configuration);
        var pg = new PostgresRegistry(new PostgresOptions { Connections = { ["Reporting"] = new PostgresConnectionOptions { Description = "R" } } }, configuration);
        var mongo = new MongoRegistry(new MongoOptions { Connections = { ["Events"] = new MongoConnectionOptions { Description = "E" } } }, configuration);

        var listing = string.Join(" ",
            sql.List().Select(c => $"{c.Name} {c.Provider} {c.Description}")
                .Concat(pg.List().Select(c => $"{c.Name} {c.Provider} {c.Description}"))
                .Concat(mongo.List().Select(c => $"{c.Name} {c.Provider} {c.Description}")));

        Assert.False(listing.Contains("CONNECTION_STRING", StringComparison.Ordinal));
    }
}
