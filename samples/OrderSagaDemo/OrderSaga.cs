using Syed.Messaging;
using Syed.Messaging.Sagas;

public class OrderSaga :
    ISagaHandler<OrderSagaState, OrderCreated>,
    ISagaHandler<OrderSagaState, PaymentCompleted>,
    ISagaHandler<OrderSagaState, PaymentTimeout>
{
    private readonly ILogger<OrderSaga> _logger;
    private readonly ISagaTimeoutScheduler _timeouts;

    public OrderSaga(ILogger<OrderSaga> logger, ISagaTimeoutScheduler timeouts)
    {
        _logger = logger;
        _timeouts = timeouts;
    }

    public async Task HandleAsync(OrderSagaState state, OrderCreated message, MessageContext ctx, CancellationToken ct)
    {
        _logger.LogInformation("OrderSaga started for OrderId={OrderId}, Total={Total}", message.OrderId, message.TotalAmount);

        state.OrderId = message.OrderId;
        state.TotalAmount = message.TotalAmount;
        state.CreatedAtUtc = DateTime.UtcNow;

        // Schedule a payment timeout in 30 seconds (for demo)
        await _timeouts.ScheduleAsync(
            sagaType: typeof(OrderSaga),
            correlationKey: message.OrderId.ToString(),
            delay: TimeSpan.FromSeconds(30),
            timeout: new PaymentTimeout(message.OrderId),
            ct: ct);
    }

    public Task HandleAsync(OrderSagaState state, PaymentCompleted message, MessageContext ctx, CancellationToken ct)
    {
        if (state.PaymentReceived)
        {
            _logger.LogInformation("PaymentCompleted received again for OrderId={OrderId}, ignoring.", message.OrderId);
            return Task.CompletedTask;
        }

        _logger.LogInformation("PaymentCompleted for OrderId={OrderId}", message.OrderId);
        state.PaymentReceived = true;
        state.CompletedAtUtc = DateTime.UtcNow;

        // Cancel timeout if you want (not implemented yet at saga helper level)
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderSagaState state, PaymentTimeout message, MessageContext ctx, CancellationToken ct)
    {
        if (state.PaymentReceived)
        {
            _logger.LogInformation("PaymentTimeout for OrderId={OrderId}, but payment already received. Ignoring timeout.", message.OrderId);
            return Task.CompletedTask;
        }

        _logger.LogWarning("PaymentTimeout fired for OrderId={OrderId}. Marking as timed out.", message.OrderId);
        state.TimedOut = true;
        state.CompletedAtUtc = DateTime.UtcNow;

        return Task.CompletedTask;
    }
}
