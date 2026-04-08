using Confluent.Kafka;
using FluentAssertions;
using Syed.Messaging.Kafka;
using Xunit;

namespace Syed.Messaging.Kafka.Tests;

public class KafkaPartitionDispatcherTests
{
    [Fact]
    public async Task Enqueue_SamePartition_PreservesOrder()
    {
        var processed = new List<int>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var dispatcher = new KafkaPartitionDispatcher<int>(
            maxConcurrentPartitions: 2,
            handler: async (_, item, _) =>
            {
                processed.Add(item);
                await Task.Yield();
            },
            ct: cts.Token);

        var partition = new TopicPartition("orders", new Partition(0));
        dispatcher.Enqueue(partition, 1).Should().BeTrue();
        dispatcher.Enqueue(partition, 2).Should().BeTrue();
        dispatcher.Enqueue(partition, 3).Should().BeTrue();

        await dispatcher.CompleteAsync(TimeSpan.FromSeconds(2));

        processed.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Enqueue_DifferentPartitions_RunConcurrentlyWhenLimitAllows()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var dispatcher = new KafkaPartitionDispatcher<int>(
            maxConcurrentPartitions: 2,
            handler: async (partition, _, token) =>
            {
                if (partition.Partition.Value == 0)
                    firstStarted.TrySetResult();
                else
                    secondStarted.TrySetResult();

                await gate.Task.WaitAsync(token);
            },
            ct: cts.Token);

        dispatcher.Enqueue(new TopicPartition("orders", new Partition(0)), 100).Should().BeTrue();
        dispatcher.Enqueue(new TopicPartition("orders", new Partition(1)), 200).Should().BeTrue();

        await firstStarted.Task.WaitAsync(cts.Token);
        await secondStarted.Task.WaitAsync(cts.Token);

        gate.TrySetResult();
        await dispatcher.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Revoke_StopsPartitionAndRejectsFurtherEnqueue()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var dispatcher = new KafkaPartitionDispatcher<int>(
            maxConcurrentPartitions: 1,
            handler: async (_, _, token) => await gate.Task.WaitAsync(token),
            ct: cts.Token);

        var partition = new TopicPartition("orders", new Partition(0));
        dispatcher.Enqueue(partition, 1).Should().BeTrue();

        dispatcher.Revoke(new[] { partition });
        dispatcher.Enqueue(partition, 2).Should().BeFalse();

        gate.TrySetResult();
        await dispatcher.CompleteAsync(TimeSpan.FromSeconds(2));
    }
}

