namespace Syed.Messaging.Kafka;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "syed-messaging-consumer";

    public string TopicPrefix { get; set; } = "app.";
    public string RetrySuffix { get; set; } = "-retry";
    public string DlqSuffix { get; set; } = "-dlq";
}
