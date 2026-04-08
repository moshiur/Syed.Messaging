using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Xunit;

namespace Syed.Messaging.Kafka.Tests;

public class KafkaRebalanceIntegrationTests
{
    [Fact]
    public async Task ConsumerGroupRebalance_Handoff_PreservesOffsetContinuity()
    {
        var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
        if (!IsKafkaAvailable(bootstrapServers))
        {
            return;
        }

        var topic = $"rebalance-it-{Guid.NewGuid():N}";
        const int messageCount = 30;
        var groupId = $"it-rebalance-{Guid.NewGuid():N}";

        await CreateTopicAsync(bootstrapServers, topic, partitions: 1);
        await ProduceSequentialMessagesAsync(bootstrapServers, topic, messageCount);

        var consumed = new List<int>();

        using var consumerA = BuildConsumer(bootstrapServers, groupId);
        consumerA.Subscribe(topic);

        // Consumer A reads an initial chunk and commits offsets.
        await ConsumeAndCommitAsync(consumerA, consumed, take: 12, timeout: TimeSpan.FromSeconds(10));

        using var consumerB = BuildConsumer(bootstrapServers, groupId);
        consumerB.Subscribe(topic);

        // Trigger rebalance by closing A, then B should continue from committed offset.
        consumerA.Close();
        await ConsumeAndCommitAsync(consumerB, consumed, take: messageCount - consumed.Count, timeout: TimeSpan.FromSeconds(20));

        consumed.Should().HaveCount(messageCount);
        consumed.Should().OnlyHaveUniqueItems();
        consumed.Should().Equal(Enumerable.Range(0, messageCount));

        await DeleteTopicAsync(bootstrapServers, topic);
    }

    private static IConsumer<string, string> BuildConsumer(string bootstrapServers, string groupId)
    {
        var cfg = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        return new ConsumerBuilder<string, string>(cfg).Build();
    }

    private static async Task CreateTopicAsync(string bootstrapServers, string topic, int partitions)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = partitions,
                ReplicationFactor = 1
            }
        });
    }

    private static async Task DeleteTopicAsync(string bootstrapServers, string topic)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
            await admin.DeleteTopicsAsync(new[] { topic });
        }
        catch
        {
            // Best-effort cleanup for local integration runs.
        }
    }

    private static async Task ProduceSequentialMessagesAsync(string bootstrapServers, string topic, int count)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        for (var i = 0; i < count; i++)
        {
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = "same-partition-key",
                Value = i.ToString()
            });
        }
    }

    private static async Task ConsumeAndCommitAsync(
        IConsumer<string, string> consumer,
        List<int> buffer,
        int take,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var target = buffer.Count + take;

        while (buffer.Count < target && DateTimeOffset.UtcNow < deadline)
        {
            var cr = consumer.Consume(TimeSpan.FromMilliseconds(250));
            if (cr is null) continue;

            if (int.TryParse(cr.Message.Value, out var parsed))
            {
                buffer.Add(parsed);
            }

            consumer.StoreOffset(cr);
            consumer.Commit(cr);
            await Task.Yield();
        }

        if (buffer.Count < target)
        {
            throw new InvalidOperationException($"Timed out consuming expected messages. Expected {target}, got {buffer.Count}.");
        }
    }

    private static bool IsKafkaAvailable(string bootstrapServers)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(2));
            return metadata.Brokers.Count > 0;
        }
        catch (Exception ex) when (ex is KafkaException || ex is TimeoutException)
        {
            return false;
        }
    }
}

