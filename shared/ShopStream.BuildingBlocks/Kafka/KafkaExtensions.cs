using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopStream.BuildingBlocks.Health;

namespace ShopStream.BuildingBlocks.Kafka;

public static class KafkaExtensions
{
    public static IServiceCollection AddShopStreamKafka(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddHealthChecks().AddCheck<KafkaHealthCheck>("kafka", tags: new[] { HealthEndpoints.ReadyTag });
        return services;
    }
}
