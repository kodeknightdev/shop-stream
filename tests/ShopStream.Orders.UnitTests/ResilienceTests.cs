using System.Net;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.Orders.Clients;

namespace ShopStream.Orders.UnitTests;

public class ResilienceTests
{
    private static ResilienceOptions Options(int retries = 2, int timeoutSeconds = 1) => new()
    {
        RetryCount = retries, TimeoutSeconds = timeoutSeconds, FailuresBeforeBreaking = 3, BreakSeconds = 30, MaxParallelization = 1, MaxQueuedActions = 0
    };

    [Fact]
    public async Task Grpc_retries_transient_failures_then_succeeds()
    {
        var policy = ResiliencePolicies.ForGrpc("catalog", Options(), NullLogger.Instance);
        var calls = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            calls++;
            return calls < 3 ? throw new RpcException(new Status(StatusCode.Unavailable, "down")) : Task.FromResult("ok");
        });

        result.Should().Be("ok");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Grpc_circuit_opens_after_repeated_failures_and_fails_fast()
    {
        var policy = ResiliencePolicies.ForGrpc("catalog", Options(retries: 0), NullLogger.Instance);
        var calls = 0;
        Task<string> Failing() { calls++; throw new RpcException(new Status(StatusCode.Unavailable, "down")); }

        for (var i = 0; i < 3; i++)
        {
            await policy.Invoking(p => p.ExecuteAsync(Failing)).Should().ThrowAsync<RpcException>();
        }

        await policy.Invoking(p => p.ExecuteAsync(Failing)).Should().ThrowAsync<BrokenCircuitException>();
        calls.Should().Be(3, "an open circuit must not call the dependency");
        ResiliencePolicies.IsDependencyFailure(new BrokenCircuitException()).Should().BeTrue();
    }

    [Fact]
    public async Task Grpc_does_not_retry_bad_requests()
    {
        var policy = ResiliencePolicies.ForGrpc("catalog", Options(), NullLogger.Instance);
        var calls = 0;
        Task<string> Invalid() { calls++; throw new RpcException(new Status(StatusCode.InvalidArgument, "bad id")); }

        await policy.Invoking(p => p.ExecuteAsync(Invalid)).Should().ThrowAsync<RpcException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Http_bulkhead_rejects_calls_over_the_limit()
    {
        var policy = ResiliencePolicies.ForHttp("cart", Options(timeoutSeconds: 5), NullLogger.Instance);
        var gate = new TaskCompletionSource<HttpResponseMessage>();

        var first = policy.ExecuteAsync(() => gate.Task);
        await policy.Invoking(p => p.ExecuteAsync(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .Should().ThrowAsync<BulkheadRejectedException>();

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        (await first).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Http_retries_5xx_responses()
    {
        var policy = ResiliencePolicies.ForHttp("cart", Options(), NullLogger.Instance);
        var calls = 0;

        var response = await policy.ExecuteAsync(() => Task.FromResult(new HttpResponseMessage(++calls < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(3);
    }
}

public class RetryRoutingTests
{
    private static readonly DateTimeOffset Now = new(2023, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "orders.order-placed.retry", 1)]
    [InlineData(1, "orders.order-placed.retry", 2)]
    [InlineData(2, "orders.order-placed.retry", 4)]
    public void Failures_go_to_the_retry_topic_with_exponential_backoff(int previous, string topic, int seconds)
    {
        var decision = RetryRouting.OnFailure("orders.order-placed", previous, maxRetries: 3, Now);
        decision.Topic.Should().Be(topic);
        decision.NotBefore.Should().Be(Now.AddSeconds(seconds));
    }

    [Fact]
    public void Exhausted_retries_go_to_the_dead_letter_topic()
    {
        var decision = RetryRouting.OnFailure("payments.payment-failed", previousAttempts: 3, maxRetries: 3, Now);
        decision.Topic.Should().Be("payments.payment-failed.dlq");
        decision.NotBefore.Should().BeNull();
    }

    [Fact]
    public void Backoff_is_capped()
    {
        RetryRouting.Backoff(20).Should().Be(RetryRouting.MaxBackoff);
        RetryRouting.SourceTopic("inventory.stock-reserved.retry").Should().Be("inventory.stock-reserved");
    }
}
