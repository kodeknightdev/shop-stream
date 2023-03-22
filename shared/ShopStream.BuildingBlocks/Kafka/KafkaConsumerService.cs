using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.Contracts;

namespace ShopStream.BuildingBlocks.Kafka;

// commits after the handler; failures go to <topic>.retry (backoff), then <topic>.dlq
// at-least-once, so handlers must be idempotent
public abstract class KafkaConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    protected readonly ILogger Logger;

    protected KafkaConsumerService(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        Logger = logger;
    }

    protected abstract IReadOnlyCollection<string> SubscribedTopics { get; }

    protected abstract Task HandleAsync(IServiceProvider services, string topic, RawEnvelope envelope, CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            Logger.LogWarning("Kafka disabled; {Consumer} will not start", GetType().Name);
            return Task.CompletedTask;
        }

        // Consume() blocks, keep it off the thread pool
        return Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false
        };

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _options.BootstrapServers, Acks = Acks.All }).Build();
        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => Logger.LogWarning("Kafka consumer error: {Reason}", e.Reason))
            .Build();

        var topics = SubscribedTopics.Concat(SubscribedTopics.Select(Topics.Retry)).ToList();
        consumer.Subscribe(topics);
        Logger.LogInformation("{Consumer} subscribed to {Topics}", GetType().Name, string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.LogWarning(ex, "Consume failed: {Reason}", ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                continue;
            }

            if (result?.Message is null)
            {
                continue;
            }

            await ProcessAsync(result, producer, stoppingToken);
            consumer.Commit(result);
        }

        consumer.Close();
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, IProducer<string, string> producer, CancellationToken ct)
    {
        var headers = result.Message.Headers;
        var fromRetry = RetryRouting.IsRetryTopic(result.Topic);
        if (fromRetry && GetHeader(headers, KafkaHeaders.ConsumerGroup) is { } group && group != _options.GroupId)
        {
            return; // another group's retry copy
        }

        var originalTopic = GetHeader(headers, KafkaHeaders.OriginalTopic) ?? RetryRouting.SourceTopic(result.Topic);
        var retryCount = int.TryParse(GetHeader(headers, KafkaHeaders.RetryCount), out var r) ? r : 0;
        var correlationId = GetHeader(headers, KafkaHeaders.CorrelationId);

        if (fromRetry && long.TryParse(GetHeader(headers, KafkaHeaders.NotBefore), out var notBeforeMs))
        {
            var wait = DateTimeOffset.FromUnixTimeMilliseconds(notBeforeMs) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait < RetryRouting.MaxBackoff ? wait : RetryRouting.MaxBackoff, ct);
            }
        }

        RawEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RawEnvelope>(result.Message.Value, JsonDefaults.Options);
            if (envelope is null || envelope.EventId == Guid.Empty)
            {
                throw new JsonException("Envelope is missing eventId");
            }
        }
        catch (JsonException ex)
        {
            var dlq = Topics.DeadLetter(originalTopic);
            Logger.LogError(ex, "Poison message on {Topic} key {Key}; sending to {Dlq}", result.Topic, result.Message.Key, dlq);
            await ForwardAsync(producer, dlq, result.Message, originalTopic, retryCount, ex.Message, null, ct);
            return;
        }

        CorrelationContext.CorrelationId = correlationId ?? envelope.CorrelationId ?? Guid.NewGuid().ToString("N");
        using var _ = LogContext.PushProperty("CorrelationId", CorrelationContext.CorrelationId);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await HandleAsync(scope.ServiceProvider, originalTopic, envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var decision = RetryRouting.OnFailure(originalTopic, retryCount, _options.MaxRetries, DateTimeOffset.UtcNow);
            Logger.LogWarning(ex, "Handler failed for {EventType} {EventId} (attempt {Attempt}); forwarding to {Target}",
                envelope.EventType, envelope.EventId, decision.Attempt, decision.Topic);
            await ForwardAsync(producer, decision.Topic, result.Message, originalTopic, decision.Attempt, ex.Message, decision.NotBefore, ct);
        }
    }

    private async Task ForwardAsync(IProducer<string, string> producer, string topic, Message<string, string> message,
        string originalTopic, int retryCount, string error, DateTimeOffset? notBefore, CancellationToken ct)
    {
        var replaced = new[] { KafkaHeaders.RetryCount, KafkaHeaders.OriginalTopic, KafkaHeaders.Error, KafkaHeaders.ConsumerGroup, KafkaHeaders.NotBefore };
        var headers = new Headers();
        foreach (var h in message.Headers ?? new Headers())
        {
            if (!replaced.Contains(h.Key))
            {
                headers.Add(h.Key, h.GetValueBytes());
            }
        }

        headers.Add(KafkaHeaders.OriginalTopic, Encoding.UTF8.GetBytes(originalTopic));
        headers.Add(KafkaHeaders.ConsumerGroup, Encoding.UTF8.GetBytes(_options.GroupId));
        headers.Add(KafkaHeaders.RetryCount, Encoding.UTF8.GetBytes(retryCount.ToString()));
        headers.Add(KafkaHeaders.Error, Encoding.UTF8.GetBytes(error.Length > 500 ? error[..500] : error));
        if (notBefore is not null)
        {
            headers.Add(KafkaHeaders.NotBefore, Encoding.UTF8.GetBytes(notBefore.Value.ToUnixTimeMilliseconds().ToString()));
        }

        await producer.ProduceAsync(topic, new Message<string, string> { Key = message.Key, Value = message.Value, Headers = headers }, ct);
    }

    private static string? GetHeader(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    protected static T Payload<T>(RawEnvelope envelope) =>
        envelope.Payload.Deserialize<T>(JsonDefaults.Options) ?? throw new JsonException($"Payload for {envelope.EventType} was empty");
}
