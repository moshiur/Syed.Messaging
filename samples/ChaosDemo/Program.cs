using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.Chaos;

// ─────────────────────────────────────────────────────────────────────────────
// ChaosDemo — broker-free demonstration of Syed.Messaging.Chaos.
//
// No RabbitMQ, no Kafka, no Docker. This pumps messages through the REAL
// ChaosMiddleware against a deliberately NON-IDEMPOTENT payment handler, so you
// can watch chaos expose the kind of bug that only shows up in production at 2am.
//
//   dotnet run --project samples/ChaosDemo
//
// Set the level with SYED_CHAOS_LEVEL (default High here so shapes fire fast):
//   SYED_CHAOS_LEVEL=medium dotnet run --project samples/ChaosDemo
// ─────────────────────────────────────────────────────────────────────────────

const int OrderCount = 150;
const decimal PricePerOrder = 49.00m;

using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = null; }));

// A deliberately non-idempotent "payment ledger". A correct handler would be
// safe to call twice for the same order. This one is NOT — every call charges.
var ledger = new Dictionary<Guid, int>();   // orderId -> times charged
decimal totalCharged = 0m;

// Default to High so the demo shows all shapes quickly. SYED_CHAOS_LEVEL overrides.
// MaxDelayInjected is capped low here so the demo runs in seconds — the production
// default is 30s, which you'd keep for a real consumer.
var options = new ChaosOptions
{
    Level = ChaosLevel.High,
    Seed = 1337,
    MaxDelayInjected = TimeSpan.FromMilliseconds(200)
};
var env = new ChaosEnvironment(options, loggerFactory.CreateLogger<ChaosEnvironment>());
var injector = new RealisticChaosInjector(options.Seed);
var middleware = new ChaosMiddleware(env, options, injector, loggerFactory.CreateLogger<ChaosMiddleware>());

// No IInboxStore registered → the Duplicate shape WILL fire (and expose the bug).
var serviceProvider = new ServiceCollection().BuildServiceProvider();

var demoLog = loggerFactory.CreateLogger("ChaosDemo");
demoLog.LogInformation("Pumping {Count} orders through a NON-idempotent payment handler at level {Level}.",
    OrderCount, env.EffectiveLevel);
demoLog.LogInformation("A correct system charges each order exactly once. Watch what chaos does.\n");

var orderIds = new List<Guid>();

for (int i = 0; i < OrderCount; i++)
{
    var orderId = Guid.NewGuid();
    orderIds.Add(orderId);

    var envelope = new MessageEnvelope
    {
        MessageType = "orders.created",
        MessageId = orderId.ToString(),
        Body = System.Text.Encoding.UTF8.GetBytes(orderId.ToString()),
        Headers = new Dictionary<string, string> { ["message-type"] = "orders.created" }
    };

    // The handler — this is the user's code that chaos is testing.
    Func<Task> handler = () =>
    {
        ledger[orderId] = ledger.GetValueOrDefault(orderId) + 1;
        totalCharged += PricePerOrder;
        return Task.CompletedTask;
    };

    try
    {
        await middleware.InvokeAsync(envelope, serviceProvider, handler);
    }
    catch (ChaosAckTimeoutException)
    {
        // In a real consumer this surfaces as a failed ack → the broker redelivers
        // → the handler runs AGAIN. Simulate that redelivery here so the demo shows
        // the real-world consequence: a non-idempotent handler double-charges.
        demoLog.LogWarning("   ↳ broker would redeliver this order after the lost ack — replaying the handler...");
        await handler();
    }
}

// ─── Report the bugs chaos exposed ───────────────────────────────────────────

var charged = ledger.Keys.Count;
var doubleCharged = ledger.Count(kv => kv.Value > 1);
var neverCharged = orderIds.Count(id => !ledger.ContainsKey(id));

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine("  CHAOS REPORT — what your non-idempotent handler actually did");
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine($"  Orders sent:              {OrderCount}");
Console.WriteLine($"  Distinct orders charged:  {charged}");
Console.WriteLine($"  Total charge events:      {ledger.Values.Sum()}  (${totalCharged:0.00})");
Console.WriteLine($"  Expected if correct:      {OrderCount} charges (${OrderCount * PricePerOrder:0.00})");
Console.WriteLine("  ──────────────────────────────────────────────────────────");
Console.WriteLine($"  🐞 DOUBLE-CHARGED orders:  {doubleCharged}   ← Duplicate / AckTimeout exposed non-idempotency");
Console.WriteLine($"  🐞 NEVER-CHARGED orders:   {neverCharged}   ← Drop exposed a lost-message gap");
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("  Every bug above is real and would happen in production. Chaos");
Console.WriteLine("  just made it happen in dev, in 5 seconds, where you can fix it.");
Console.WriteLine();
Console.WriteLine("  Fixes: make the handler idempotent (check a processed-set / use");
Console.WriteLine("  the EF Core inbox), and ensure upstream retries cover drops.");
Console.WriteLine();
