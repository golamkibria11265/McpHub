using McpHub.Core.Tools;
using McpHub.Host;
using McpHub.Host.Hosting;
using McpHub.Host.Mcp;
using McpHub.Host.Modules;
using McpHub.Host.Security;
using McpHub.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

// Composition root only: no business logic lives here.

// Before anything can fail: an unhandled exception ends the process instead of leaving it running.

var builder = WebApplication.CreateBuilder(args);

// Configuration (including credentials) comes from appsettings.json, then appsettings.{Environment}.json
// (Development or Production), loaded automatically by the host. Environment variables still override both.

// Structured JSON logs to stdout; systemd/journald (or your log shipper) collects them.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

// Behind Nginx on the same machine: trust X-Forwarded-* from loopback only (the framework default).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddMcpHubInfrastructure();
builder.Services.AddMcpHubSecurity(builder.Configuration, builder.Environment);
builder.Services.AddMcpHubRateLimiting(builder.Configuration);
builder.Services.AddScoped<IToolExecutor, ToolExecutor>();
builder.Services.AddHealthChecks();

// Stateless Streamable HTTP: no per-session server memory, so instances can be restarted or load-balanced freely.
var mcp = builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = "McpHub", Version = "1.0.0" })
    .WithHttpTransport(options => options.Stateless = true);

mcp.AddIntegrationModules(builder.Services, builder.Configuration);

var app = builder.Build();

// First, so it wraps everything below: unhandled exceptions are logged and answered with a generic 500.
app.UseMcpHubExceptionHandler();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Health endpoints are for the local monitor / reverse proxy; Nginx must not expose them publicly.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapOAuthProtectedResourceMetadata();
app.MapMcp("/mcp")
    .RequireAuthorization()
    .RequireRateLimiting(RateLimitingExtensions.PolicyName);

var loaded = app.Services.GetRequiredService<LoadedModules>();

// Landing page for local development only (Production keeps "/" unmapped, so nothing extra is exposed).
//if (app.Environment.IsDevelopment())
//{
//    app.MapGet("/", () => Results.Content(
//        LandingPage.Render(app.Environment.EnvironmentName, loaded.Names), "text/html"))
//        .AllowAnonymous();
//}
app.MapGet("/", () => Results.Content(
    LandingPage.Render(app.Environment.EnvironmentName, loaded.Names), "text/html"))
    .AllowAnonymous();

if (loaded.Names.Count == 0)
{
    app.Logger.LogWarning("No integration modules are enabled. Set Modules:<Name>:Enabled=true to expose tools.");
}
else
{
    app.Logger.LogInformation("Enabled modules: {Modules}", string.Join(", ", loaded.Names));
}
app.Run();
