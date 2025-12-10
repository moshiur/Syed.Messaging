using System.Collections.Concurrent;
using Syed.Messaging;

namespace Syed.Messaging.Sagas;

public interface ISagaStateStore<TSagaState>
    where TSagaState : class, ISagaState, new()
{
    Task<TSagaState?> LoadAsync(string correlationKey, CancellationToken ct);
    Task SaveAsync(TSagaState state, string correlationKey, CancellationToken ct);
}

/// <summary>
/// Simple in-memory saga state store for demos and tests. Not suitable for production.
/// </summary>
public sealed class InMemorySagaStateStore<TSagaState> : ISagaStateStore<TSagaState>
    where TSagaState : class, ISagaState, new()
{
    private readonly ConcurrentDictionary<string, TSagaState> _states = new();

    public Task<TSagaState?> LoadAsync(string correlationKey, CancellationToken ct)
    {
        _states.TryGetValue(correlationKey, out var state);
        return Task.FromResult(state);
    }

    public Task SaveAsync(TSagaState state, string correlationKey, CancellationToken ct)
    {
        _states[correlationKey] = state;
        return Task.CompletedTask;
    }
}
