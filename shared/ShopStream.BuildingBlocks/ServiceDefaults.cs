using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Logging;

namespace ShopStream.BuildingBlocks;

public static class ServiceDefaults
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName, Type validatorsAssemblyMarker)
    {
        builder.AddShopStreamLogging(serviceName);
        builder.Services.AddShopStreamProblemDetails();
        builder.Services.AddShopStreamSwagger($"shop-stream {serviceName}");
        builder.Services.AddHealthChecks();
        builder.Services.AddValidatorsFromAssemblyContaining(validatorsAssemblyMarker);
        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information);
        app.UseShopStreamExceptionHandler();
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }
}
