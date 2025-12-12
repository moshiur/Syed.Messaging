using Microsoft.EntityFrameworkCore;
using Syed.Messaging;
using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.EfCore;

/// <summary>
/// Entity representing a persisted saga state with serialized JSON data.
/// </summary>
public class SagaStateEntity
{
    /// <summary>
    /// Unique identifier for this saga instance.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The saga type name (e.g., "OrderSaga").
    /// </summary>
    public string SagaType { get; set; } = default!;

    /// <summary>
    /// The correlation key for this saga instance.
    /// </summary>
    public string CorrelationKey { get; set; } = default!;

    /// <summary>
    /// Serialized JSON representation of the saga state.
    /// </summary>
    public string StateJson { get; set; } = default!;

    /// <summary>
    /// Optimistic concurrency version.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Timestamp when the saga was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Timestamp when the saga was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Whether the saga has completed.
    /// </summary>
    public bool IsCompleted { get; set; }
}

/// <summary>
/// EF Core implementation of ISagaStateStore that persists saga state as JSON.
/// </summary>
public class EfSagaStateStore<TContext, TSagaState> : ISagaStateStore<TSagaState>
    where TContext : DbContext
    where TSagaState : class, ISagaState, new()
{
    private readonly TContext _db;
    private readonly ISerializer _serializer;
    private readonly string _sagaTypeName;

    public EfSagaStateStore(TContext db, ISerializer serializer)
    {
        _db = db;
        _serializer = serializer;
        _sagaTypeName = typeof(TSagaState).Name;
    }

    public async Task<TSagaState?> LoadAsync(string correlationKey, CancellationToken ct)
    {
        var entity = await _db.Set<SagaStateEntity>()
            .FirstOrDefaultAsync(x => 
                x.SagaType == _sagaTypeName && 
                x.CorrelationKey == correlationKey &&
                !x.IsCompleted, ct);

        if (entity is null)
            return null;

        var state = System.Text.Json.JsonSerializer.Deserialize<TSagaState>(entity.StateJson);
        if (state is not null)
        {
            state.Id = entity.Id;
            state.Version = entity.Version;
        }
        return state;
    }

    public async Task SaveAsync(TSagaState state, string correlationKey, CancellationToken ct)
    {
        var entity = await _db.Set<SagaStateEntity>()
            .FirstOrDefaultAsync(x => 
                x.SagaType == _sagaTypeName && 
                x.CorrelationKey == correlationKey, ct);

        var stateJson = System.Text.Json.JsonSerializer.Serialize(state);
        var now = DateTime.UtcNow;

        if (entity is null)
        {
            // Create new saga state
            entity = new SagaStateEntity
            {
                Id = state.Id == Guid.Empty ? Guid.NewGuid() : state.Id,
                SagaType = _sagaTypeName,
                CorrelationKey = correlationKey,
                StateJson = stateJson,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsCompleted = false
            };
            _db.Set<SagaStateEntity>().Add(entity);

            // Update state with persisted values
            state.Id = entity.Id;
            state.Version = entity.Version;
        }
        else
        {
            // Update existing saga state with optimistic concurrency
            if (entity.Version != state.Version)
            {
                throw new DbUpdateConcurrencyException(
                    $"Saga state for '{_sagaTypeName}' with correlation key '{correlationKey}' " +
                    $"has been modified. Expected version {state.Version}, found {entity.Version}.");
            }

            entity.StateJson = stateJson;
            entity.Version++;
            entity.UpdatedAtUtc = now;

            // Update state with new version
            state.Version = entity.Version;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks a saga as completed. Completed sagas won't be loaded again.
    /// </summary>
    public async Task MarkCompletedAsync(string correlationKey, CancellationToken ct)
    {
        var entity = await _db.Set<SagaStateEntity>()
            .FirstOrDefaultAsync(x => 
                x.SagaType == _sagaTypeName && 
                x.CorrelationKey == correlationKey, ct);

        if (entity is not null)
        {
            entity.IsCompleted = true;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
