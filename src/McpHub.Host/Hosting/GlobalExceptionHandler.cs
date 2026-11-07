using Microsoft.AspNetCore.Diagnostics;

namespace McpHub.Host.Hosting;

internal static class GlobalExceptionHandler
{
    /// <summary>
    /// Last line of defence for every HTTP request. The full exception goes to the log; the caller only gets a generic
    /// 500 with a trace id, so internals (paths, SQL, tokens) never leak. Tool failures are already translated by
    /// ToolExecutor, so this only sees what slipped past it. Register it first so it wraps the whole pipeline.
    /// </summary>
    public static IApplicationBuilder UseMcpHubExceptionHandler(this IApplicationBuilder app) =>
        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            var failure = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("McpHub.UnhandledException");

            // A caller that hung up is not an error. The query string is not logged: it can carry secrets.
            if (failure is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            logger.LogError(failure, "Unhandled exception for {Method} {Path} (trace {TraceId})",context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                new
                {
                    type = "about:blank",
                    title = "An unexpected error occurred.",
                    status = StatusCodes.Status500InternalServerError,
                    traceId = context.TraceIdentifier
                },
                options: null,
                contentType: "application/problem+json");
        }));
}
