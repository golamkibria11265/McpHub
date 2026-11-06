using McpHub.Core.Auditing;
using McpHub.Core.Errors;
using McpHub.Core.Security;
using McpHub.Host.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using NSubstitute;

namespace McpHub.UnitTests.Host;

public class ToolExecutorTests
{
    private readonly ICallerContext _caller = Substitute.For<ICallerContext>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();

    private ToolExecutor Create(bool hasScope)
    {
        _caller.UserId.Returns("user-1");
        _caller.HasScope("github.read").Returns(hasScope);
        return new ToolExecutor(_caller, _audit, NullLogger<ToolExecutor>.Instance);
    }

    [Fact]
    public async Task Success_returns_result_and_audits()
    {
        var result = await Create(hasScope: true).ExecuteAsync("t", "github.read", () => Task.FromResult(42));

        Assert.Equal(42, result);
        _audit.Received(1).Record(Arg.Is<AuditEntry>(e =>
            e.Outcome == AuditOutcome.Success && e.UserId == "user-1" && e.Tool == "t"));
    }

    [Fact]
    public async Task Missing_scope_is_denied_and_action_never_runs()
    {
        var executed = false;

        await Assert.ThrowsAsync<McpException>(() =>
            Create(hasScope: false).ExecuteAsync("t", "github.read", () =>
            {
                executed = true;
                return Task.FromResult(1);
            }));

        Assert.False(executed);
        _audit.Received(1).Record(Arg.Is<AuditEntry>(e => e.Outcome == AuditOutcome.Denied));
    }

    [Fact]
    public async Task Integration_errors_surface_their_safe_message()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            Create(hasScope: true).ExecuteAsync<int>("t", "github.read",
                () => throw new IntegrationException(IntegrationErrorKind.NotFound, "GitHub", "Not found.")));

        Assert.Equal("[GitHub] Not found.", ex.Message);
        _audit.Received(1).Record(Arg.Is<AuditEntry>(e => e.Outcome == AuditOutcome.Failed && e.ErrorCode == "NotFound"));
    }

    [Fact]
    public async Task Unexpected_errors_never_leak_internal_details()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            Create(hasScope: true).ExecuteAsync<int>("t", "github.read",
                () => throw new InvalidOperationException("connection string Server=prod;Password=hunter2")));

        Assert.DoesNotContain("hunter2", ex.Message, StringComparison.Ordinal);
    }
}
