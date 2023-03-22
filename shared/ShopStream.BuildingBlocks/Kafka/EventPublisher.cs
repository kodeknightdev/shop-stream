using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.Contracts;

namespace ShopStream.BuildingBlocks.Kafka;

public interface IEventPublisher
{
    Task PublishAsync<T>(string topic, string key, string eventType, T payload, CancellationToken ct = default);

    Task PublishRawAsync(string topic, string key, string eventType, string envelopeJson, string? correlationId, CancellationToken ct = default);
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string>? _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        if (!options.Value.Enabled)
        {
            return;
        }

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public static string Serialize<T>(string eventType, T payload, string? correlationId) =>
        JsonSerializer.Serialize(EventEnvelope<T>.Create(eventType, payload, correlationId), JsonDefaults.Options);

    public Task PublishAsync<T>(string topic, string key, string eventType, T payload, CancellationToken ct = default)
    {
        var correlationId = CorrelationContext.GetOrCreate();
        return PublishRawAsync(topic, key, eventType, Serialize(eventType, payload, correlationId), correlationId, ct);
    }

    public async Task PublishRawAsync(string topic, string key, string eventType, string envelopeJson, string? correlationId, CancellationToken ct = default)
    {
        if (_producer is null)
        {
            _logger.LogDebug("Kafka disabled, dropping {EventType} for {Topic}", eventType, topic);
            return;
        }

        var headers = new Headers
        {
            { KafkaHeaders.EventType, Encoding.UTF8.GetBytes(eventType) },
            { KafkaHeaders.SchemaVersion, Encoding.UTF8.GetBytes("1") }
        };
        if (!string.IsNullOrEmpty(correlationId))
        {
            headers.Add(KafkaHeaders.CorrelationId, Encoding.UTF8.GetBytes(correlationId));
        }

        var result = await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = envelopeJson, Headers = headers }, ct);
        _logger.LogInformation("Published {EventType} to {Topic} [{Partition}@{Offset}] key {Key}",
            eventType, topic, result.Partition.Value, result.Offset.Value, key);
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}
