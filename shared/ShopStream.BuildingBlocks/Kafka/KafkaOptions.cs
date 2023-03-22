namespace ShopStream.BuildingBlocks.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9094";

    public string GroupId { get; set; } = string.Empty;

    public int MaxRetries { get; set; } = 3;
}
