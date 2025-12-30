namespace Syed.Messaging.Kafka;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "syed-messaging-consumer";

    public string TopicPrefix { get; set; } = "app.";
    public string RetrySuffix { get; set; } = "-retry";
    public string DlqSuffix { get; set; } = "-dlq";

    /// <summary>
    /// Delay intervals for retry topics (in seconds).
    /// Messages will be published to retry topics with corresponding delays.
    /// </summary>
    public int[] RetryDelaysSeconds { get; set; } = { 30, 60, 300 };

    /// <summary>
    /// Whether to enable delayed retry topics (retry-30s, retry-60s, etc).
    /// </summary>
    public bool EnableDelayedRetry { get; set; } = false;
}
