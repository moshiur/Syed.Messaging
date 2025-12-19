using System.ComponentModel.DataAnnotations;

namespace Syed.Messaging.Inbox.EfCore;

public class InboxMessage
{
    [Key]
    [MaxLength(200)]
    public string MessageId { get; set; } = default!;

    public DateTimeOffset ProcessedAt { get; set; }

    public DateTimeOffset? Expiration { get; set; }
}
