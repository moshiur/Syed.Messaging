using Microsoft.EntityFrameworkCore;
using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.EfCore;

/// <summary>
/// Entity representing a persisted saga timeout.
/// </summary>
public class SagaTimeoutEntity
{
    public Guid Id { get; set; }
    public string SagaTypeKey { get; set; } = default!;
    public string TimeoutTypeKey { get; set; } = default!;
    public string? TimeoutTypeVersion { get; set; }
    public string CorrelationKey { get; set; } = default!;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public DateTime DueAtUtc { get; set; }
    public bool Cancelled { get; set; }
    public bool Processed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}

/// <summary>
/// EF Core implementation of ISagaTimeoutStore for persistent timeout scheduling.
/// </summary>
public class EfSagaTimeoutStore<TContext> : ISagaTimeoutStore
    where TContext : DbContext
{
    private readonly TContext _db;

    public EfSagaTimeoutStore(TContext db)
    {
        _db = db;
    }

    public async Task ScheduleAsync(SagaTimeoutRequest request, CancellationToken ct)
    {
        var entity = new SagaTimeoutEntity
        {
            Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
            SagaTypeKey = request.SagaTypeKey,
            TimeoutTypeKey = request.TimeoutTypeKey,
            TimeoutTypeVersion = request.TimeoutTypeVersion,
            CorrelationKey = request.CorrelationKey,
            Payload = request.Payload,
            DueAtUtc = request.DueAtUtc,
            Cancelled = request.Cancelled,
            Processed = false,
            CreatedAtUtc = request.CreatedAtUtc
        };

        _db.Set<SagaTimeoutEntity>().Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(
        string sagaTypeKey,
        string correlationKey,
        string timeoutTypeKey,
        string? timeoutTypeVersion,
        CancellationToken ct)
    {
        var timeouts = await _db.Set<SagaTimeoutEntity>()
            .Where(x =>
                x.SagaTypeKey == sagaTypeKey &&
                x.CorrelationKey == correlationKey &&
                x.TimeoutTypeKey == timeoutTypeKey &&
                x.TimeoutTypeVersion == timeoutTypeVersion &&
                !x.Cancelled &&
                !x.Processed)
            .ToListAsync(ct);

        foreach (var timeout in timeouts)
        {
            timeout.Cancelled = true;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SagaTimeoutRequest>> GetDueAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct)
    {
        var entities = await _db.Set<SagaTimeoutEntity>()
            .Where(x => !x.Cancelled && !x.Processed && x.DueAtUtc <= nowUtc)
            .OrderBy(x => x.DueAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        return entities.Select(e => new SagaTimeoutRequest
        {
            Id = e.Id,
            SagaTypeKey = e.SagaTypeKey,
            TimeoutTypeKey = e.TimeoutTypeKey,
            TimeoutTypeVersion = e.TimeoutTypeVersion,
            CorrelationKey = e.CorrelationKey,
            Payload = e.Payload,
            DueAtUtc = e.DueAtUtc,
            Cancelled = e.Cancelled,
            CreatedAtUtc = e.CreatedAtUtc
        }).ToList();
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Set<SagaTimeoutEntity>()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is not null)
        {
            entity.Processed = true;
            entity.ProcessedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
