using Confluent.Kafka;

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

    /// <summary>
    /// Consumer behavior knobs used by KafkaTransport when subscribing.
    /// Defaults keep current behavior unless explicitly overridden.
    /// </summary>
    public KafkaConsumerOptions Consumer { get; set; } = new();
}

public sealed class KafkaConsumerOptions
{
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;
    public bool EnableAutoCommit { get; set; } = true;
    public bool EnableAutoOffsetStore { get; set; } = true;
    public int MaxConcurrentPartitions { get; set; } = 1;
    public int? MaxPollIntervalMs { get; set; }
    public int? SessionTimeoutMs { get; set; }
    public int? HeartbeatIntervalMs { get; set; }
    public bool LogRebalanceEvents { get; set; } = true;
    public bool UseStaticGroupMembership { get; set; } = false;
    public string? GroupInstanceId { get; set; }
    public KafkaPartitionAssignmentStrategy PartitionAssignmentStrategy { get; set; } = KafkaPartitionAssignmentStrategy.CooperativeSticky;
}

public enum KafkaPartitionAssignmentStrategy
{
    Range,
    RoundRobin,
    CooperativeSticky
}
