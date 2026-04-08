using System.Collections.Concurrent;
using System.Threading.Channels;
using Confluent.Kafka;

namespace Syed.Messaging.Kafka;

internal sealed class KafkaPartitionDispatcher<T>
{
    private readonly ConcurrentDictionary<TopicPartition, Channel<T>> _channels = new();
    private readonly ConcurrentDictionary<TopicPartition, Task> _workers = new();
    private readonly SemaphoreSlim _partitionConcurrency;
    private readonly Func<TopicPartition, T, CancellationToken, Task> _handler;
    private readonly CancellationToken _ct;

    public KafkaPartitionDispatcher(
        int maxConcurrentPartitions,
        Func<TopicPartition, T, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        _partitionConcurrency = new SemaphoreSlim(Math.Max(1, maxConcurrentPartitions));
        _handler = handler;
        _ct = ct;
    }

    public bool Enqueue(TopicPartition partition, T item)
    {
        var channel = EnsureWorker(partition);
        return channel.Writer.TryWrite(item);
    }

    public void Revoke(IEnumerable<TopicPartition> partitions)
    {
        foreach (var partition in partitions)
        {
            if (_channels.TryGetValue(partition, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    public async Task CompleteAsync(TimeSpan timeout)
    {
        foreach (var channel in _channels.Values)
        {
            channel.Writer.TryComplete();
        }

        var tasks = _workers.Values.ToArray();
        if (tasks.Length == 0) return;

        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout, _ct);
        }
        catch (TimeoutException)
        {
            // Best effort drain; caller controls follow-up behavior.
        }
    }

    private Channel<T> EnsureWorker(TopicPartition partition)
    {
        var channel = _channels.GetOrAdd(partition, _ => Channel.CreateUnbounded<T>());

        _workers.GetOrAdd(partition, _ => Task.Run(async () =>
        {
            await _partitionConcurrency.WaitAsync(_ct);
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(_ct))
                {
                    await _handler(partition, item, _ct);
                }
            }
            finally
            {
                _partitionConcurrency.Release();
                _channels.TryRemove(partition, out Channel<T>? _);
                _workers.TryRemove(partition, out Task? _);
            }
        }, _ct));

        return channel;
    }
}

