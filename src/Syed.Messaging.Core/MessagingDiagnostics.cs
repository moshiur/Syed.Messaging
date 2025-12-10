using System.Diagnostics;

namespace Syed.Messaging;

public static class MessagingDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("Syed.Messaging");

    public const string PublishActivityName = "Syed.Messaging.Publish";
    public const string ConsumeActivityName = "Syed.Messaging.Consume";
}
