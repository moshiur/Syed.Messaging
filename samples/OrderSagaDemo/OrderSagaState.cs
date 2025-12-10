using Syed.Messaging;

public class OrderSagaState : ISagaState
{
    public Guid Id { get; set; }
    public int Version { get; set; }

    public Guid OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public bool PaymentReceived { get; set; }
    public bool TimedOut { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
