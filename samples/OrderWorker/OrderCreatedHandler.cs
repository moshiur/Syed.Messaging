using Syed.Messaging;

public class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Worker received OrderCreated: OrderId={OrderId}, CustomerId={CustomerId}, Retry={RetryCount}",
            message.OrderId, message.CustomerId, context.RetryCount);

        return Task.CompletedTask;
    }
}
