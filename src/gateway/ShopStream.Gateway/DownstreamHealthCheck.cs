using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace ShopStream.Gateway;

// degraded rather than unhealthy, so one service down doesn't pull the gateway
public sealed class DownstreamHealthCheck : IHealthCheck
{
    private readonly IProxyConfigProvider _config;
    private readonly IHttpClientFactory _httpFactory;

    public DownstreamHealthCheck(IProxyConfigProvider config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var client = _httpFactory.CreateClient("downstream-health");
        var clusters = _config.GetConfig().Clusters;

        var results = await Task.WhenAll(clusters.Select(async cluster =>
        {
            var address = cluster.Destinations?.Values.FirstOrDefault()?.Address;
            if (address is null)
            {
                return (cluster.ClusterId, Healthy: false);
            }

            try
            {
                using var response = await client.GetAsync(new Uri(new Uri(address), "/health"), cancellationToken);
                return (cluster.ClusterId, Healthy: response.IsSuccessStatusCode);
            }
            catch
            {
                return (cluster.ClusterId, Healthy: false);
            }
        }));

        var data = results.ToDictionary(r => r.ClusterId, r => (object)(r.Healthy ? "up" : "down"));
        return results.All(r => r.Healthy)
            ? HealthCheckResult.Healthy("All services reachable", data)
            : HealthCheckResult.Degraded("Some services are unreachable", data: data);
    }
}
