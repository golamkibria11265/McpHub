using McpHub.Core.Errors;
using McpHub.Core.Security;
using McpHub.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpHub.UnitTests.Infrastructure;

public class CredentialProviderTests
{
    private static ICredentialProvider Create(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        services.AddMcpHubInfrastructure();
        return services.BuildServiceProvider().GetRequiredService<ICredentialProvider>();
    }

    [Fact]
    public async Task Reads_the_credential_from_inside_the_module_section()
    {
        var provider = Create(new() { ["Modules:GitHub:Credentials:Token"] = "[TOKEN]" });

        Assert.Equal("[TOKEN]", await provider.GetAsync("GitHub"));
    }

    [Fact]
    public async Task Module_section_wins_over_the_legacy_top_level_section()
    {
        var provider = Create(new()
        {
            ["Modules:GitHub:Credentials:Token"] = "[NEW]",
            ["Credentials:GitHub:Token"] = "[OLD]"
        });

        Assert.Equal("[NEW]", await provider.GetAsync("GitHub"));
    }

    [Fact]
    public async Task Legacy_top_level_section_still_works_so_existing_settings_files_keep_running()
    {
        var provider = Create(new() { ["Credentials:GitHub:Token"] = "[OLD]" });

        Assert.Equal("[OLD]", await provider.GetAsync("GitHub"));
    }

    [Fact]
    public async Task Missing_credential_fails_with_a_safe_message()
    {
        var provider = Create(new());

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => provider.GetAsync("GitHub").AsTask());

        Assert.Equal(IntegrationErrorKind.Unauthorized, ex.Kind);
    }
}
