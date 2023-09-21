using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopStream.BuildingBlocks.Correlation;

namespace ShopStream.BuildingBlocks.Errors;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddShopStreamProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
                ctx.ProblemDetails.Extensions["correlationId"] = CorrelationContext.CorrelationId ?? ctx.HttpContext.TraceIdentifier;
            };
        });

    public static IApplicationBuilder UseShopStreamExceptionHandler(this IApplicationBuilder app)
    {
        app.UseMiddleware<ProblemDetailsMiddleware>();
        app.UseStatusCodePages();
        return app;
    }
}

internal sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted && exception is not OperationCanceledException)
        {
            var problem = exception switch
            {
                AppException app => new ProblemDetails { Status = app.StatusCode, Title = app.Title, Detail = app.Message },
                BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message },
                UnauthorizedAccessException => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" },
                _ => new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred",
                    Detail = _env.IsDevelopment() ? exception.Message : null
                }
            };

            if (problem.Status >= 500)
            {
                _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.LogInformation("{Method} {Path} -> {Status}: {Detail}", context.Request.Method, context.Request.Path, problem.Status, problem.Detail);
            }

            // IProblemDetailsService writes nothing when there's no Accept header (.NET 7)
            problem.Instance = context.Request.Path;
            problem.Extensions["correlationId"] = CorrelationContext.CorrelationId ?? context.TraceIdentifier;
            context.Response.Clear();
            await Results.Problem(problem).ExecuteAsync(context);
        }
    }
}
