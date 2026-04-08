using Confluent.Kafka;
using FluentAssertions;
using Syed.Messaging.Kafka;
using Xunit;

namespace Syed.Messaging.Kafka.Tests;

public class KafkaRebalanceFlowTests
{
    [Fact]
    public async Task Rebalance_HandoffBetweenWorkers_PreservesOffsetContinuity()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var partition = new TopicPartition("orders.created", new Partition(0));
        var processedOffsets = new List<long>();
        var sync = new object();

        var workerA = new KafkaPartitionDispatcher<KafkaTestRecord>(
            maxConcurrentPartitions: 1,
            handler: async (_, record, _) =>
            {
                lock (sync) processedOffsets.Add(record.Offset);
                await Task.Yield();
            },
            ct: cts.Token);

        // Worker A initially owns partition 0.
        foreach (var offset in Enumerable.Range(0, 10))
        {
            workerA.Enqueue(partition, new KafkaTestRecord(offset, "worker-a")).Should().BeTrue();
        }

        // Rebalance: worker A loses partition 0, worker B gains it.
        workerA.Revoke(new[] { partition });
        await workerA.CompleteAsync(TimeSpan.FromSeconds(2));

        var workerB = new KafkaPartitionDispatcher<KafkaTestRecord>(
            maxConcurrentPartitions: 1,
            handler: async (_, record, _) =>
            {
                lock (sync) processedOffsets.Add(record.Offset);
                await Task.Yield();
            },
            ct: cts.Token);

        foreach (var offset in Enumerable.Range(10, 10))
        {
            workerB.Enqueue(partition, new KafkaTestRecord(offset, "worker-b")).Should().BeTrue();
        }

        await workerB.CompleteAsync(TimeSpan.FromSeconds(2));

        processedOffsets.Should().HaveCount(20);
        processedOffsets.Should().OnlyHaveUniqueItems();
        processedOffsets.Should().Equal(Enumerable.Range(0, 20).Select(i => (long)i));
    }

    [Fact]
    public async Task Rebalance_RevokedWorkerRejectsFurtherMessages_ForRevokedPartition()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var partition = new TopicPartition("orders.created", new Partition(1));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var dispatcher = new KafkaPartitionDispatcher<KafkaTestRecord>(
            maxConcurrentPartitions: 1,
            handler: async (_, _, token) => await gate.Task.WaitAsync(token),
            ct: cts.Token);

        dispatcher.Enqueue(partition, new KafkaTestRecord(1, "worker-a")).Should().BeTrue();

        dispatcher.Revoke(new[] { partition });
        dispatcher.Enqueue(partition, new KafkaTestRecord(2, "worker-a")).Should().BeFalse();

        gate.TrySetResult();
        await dispatcher.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    private sealed record KafkaTestRecord(long Offset, string WorkerId);
}

