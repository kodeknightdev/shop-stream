using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
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
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ShopStream.Errors");

            var problem = exception switch
            {
                AppException app => new ProblemDetails { Status = app.StatusCode, Title = app.Title, Detail = app.Message },
                BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message },
                _ => new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred",
                    Detail = env.IsDevelopment() ? exception?.Message : null
                }
            };

            if (problem.Status >= 500)
            {
                logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);
            }

            context.Response.StatusCode = problem.Status ?? 500;
            var writer = context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await writer.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
        }));
        app.UseStatusCodePages();
        return app;
    }
}
