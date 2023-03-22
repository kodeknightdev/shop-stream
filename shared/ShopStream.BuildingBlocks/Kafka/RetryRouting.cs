using ShopStream.Contracts;

namespace ShopStream.BuildingBlocks.Kafka;

public sealed record RetryDecision(string Topic, int Attempt, DateTimeOffset? NotBefore);

// <topic>.retry with 1s, 2s, 4s... (max 30s) backoff, then <topic>.dlq
public static class RetryRouting
{
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Max(0, attempt - 1))));

    public static RetryDecision OnFailure(string originalTopic, int previousAttempts, int maxRetries, DateTimeOffset now)
    {
        var attempt = previousAttempts + 1;
        return attempt > maxRetries
            ? new RetryDecision(Topics.DeadLetter(originalTopic), attempt, null)
            : new RetryDecision(Topics.Retry(originalTopic), attempt, now + Backoff(attempt));
    }

    public static bool IsRetryTopic(string topic) => topic.EndsWith(Topics.RetrySuffix, StringComparison.Ordinal);

    public static string SourceTopic(string retryTopic) =>
        IsRetryTopic(retryTopic) ? retryTopic[..^Topics.RetrySuffix.Length] : retryTopic;
}
