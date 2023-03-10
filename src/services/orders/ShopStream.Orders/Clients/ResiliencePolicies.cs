using Grpc.Core;
using Polly;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace ShopStream.Orders.Clients;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 3;
    public int FailuresBeforeBreaking { get; set; } = 5;
    public int BreakSeconds { get; set; } = 30;
    public int MaxParallelization { get; set; } = 20;
    public int MaxQueuedActions { get; set; } = 40;
}

// bulkhead -> retry -> circuit breaker -> timeout. One instance per dependency so the
// breaker state is shared.
public static class ResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> ForHttp(string dependency, ResilienceOptions o, ILogger logger)
    {
        var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(o.TimeoutSeconds));

        var breaker = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(o.FailuresBeforeBreaking, TimeSpan.FromSeconds(o.BreakSeconds),
                (outcome, wait) => logger.LogWarning("Circuit to {Dependency} opened for {Seconds}s: {Reason}",
                    dependency, wait.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()),
                () => logger.LogInformation("Circuit to {Dependency} closed", dependency));

        var retry = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(o.RetryCount, Backoff);

        var bulkhead = Policy.BulkheadAsync<HttpResponseMessage>(o.MaxParallelization, o.MaxQueuedActions);

        return Policy.WrapAsync(bulkhead, retry, breaker, timeout);
    }

    public static IAsyncPolicy ForGrpc(string dependency, ResilienceOptions o, ILogger logger)
    {
        var timeout = Policy.TimeoutAsync(TimeSpan.FromSeconds(o.TimeoutSeconds));

        var breaker = Policy.Handle<RpcException>(IsTransient)
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(o.FailuresBeforeBreaking, TimeSpan.FromSeconds(o.BreakSeconds),
                (ex, wait) => logger.LogWarning("Circuit to {Dependency} opened for {Seconds}s: {Reason}", dependency, wait.TotalSeconds, ex.Message),
                () => logger.LogInformation("Circuit to {Dependency} closed", dependency));

        var retry = Policy.Handle<RpcException>(IsTransient)
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(o.RetryCount, Backoff);

        var bulkhead = Policy.BulkheadAsync(o.MaxParallelization, o.MaxQueuedActions);

        return Policy.WrapAsync(bulkhead, retry, breaker, timeout);
    }

    // down / overloaded, as opposed to a bad request
    public static bool IsDependencyFailure(Exception ex) => ex is BrokenCircuitException or BulkheadRejectedException
        or TimeoutRejectedException or HttpRequestException || ex is RpcException rpc && IsTransient(rpc);

    private static bool IsTransient(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted or StatusCode.Internal;

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 100));
}

public sealed record CatalogResilience(IAsyncPolicy Policy);
