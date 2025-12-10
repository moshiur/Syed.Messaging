public record OrderCreated(Guid OrderId, decimal TotalAmount);
public record PaymentCompleted(Guid OrderId);
public record PaymentTimeout(Guid OrderId);
