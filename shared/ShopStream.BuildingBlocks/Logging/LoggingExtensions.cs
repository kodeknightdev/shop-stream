using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ShopStream.BuildingBlocks.Logging;

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddShopStreamLogging(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((ctx, services, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName);

            if (ctx.HostingEnvironment.IsDevelopment() && ctx.Configuration["Logging:Format"] == "text")
            {
                cfg.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                cfg.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        return builder;
    }
}
