using FluentAssertions;
using Syed.Messaging;
using Xunit;

namespace Syed.Messaging.Tests;

public class MessagingMetricsDeadLetterTagsTests
{
    [Theory]
    [InlineData("kafka")]
    [InlineData("rabbitmq")]
    [InlineData("azureservicebus")]
    public void BuildDeadLetterTags_ShouldEmitRequiredTagKeys_ForAllTransports(string transport)
    {
        var tags = MessagingMetrics.BuildDeadLetterTags(
            transport: transport,
            destination: "orders.created",
            messageType: "OrderCreated",
            reason: MessagingMetrics.DlqReasonHandlerException);

        tags.Should().HaveCount(4);
        tags.Select(t => t.Key).Should().Contain(["transport", "destination", "message_type", "reason"]);
        tags.Single(t => t.Key == "transport").Value.Should().Be(transport);
        tags.Single(t => t.Key == "destination").Value.Should().Be("orders.created");
        tags.Single(t => t.Key == "message_type").Value.Should().Be("OrderCreated");
        tags.Single(t => t.Key == "reason").Value.Should().Be(MessagingMetrics.DlqReasonHandlerException);
    }

    [Fact]
    public void BuildDeadLetterTags_ShouldNormalizeDestination_AndReason()
    {
        var tags = MessagingMetrics.BuildDeadLetterTags(
            transport: "Kafka",
            destination: "Orders/tenant-123/7b40579f-7b5b-4aa5-8c72-9610bb0d2a7e/987654321",
            messageType: "OrderCreated",
            reason: "retry_exhausted");

        tags.Single(t => t.Key == "transport").Value.Should().Be("kafka");
        tags.Single(t => t.Key == "reason").Value.Should().Be(MessagingMetrics.DlqReasonMaxRetryExhausted);
        tags.Single(t => t.Key == "destination").Value!.ToString().Should().NotContain("7b40579f-7b5b-4aa5-8c72-9610bb0d2a7e");
        tags.Single(t => t.Key == "destination").Value!.ToString().Should().Contain("{id}");
        tags.Single(t => t.Key == "destination").Value!.ToString().Should().Contain("{n}");
    }

    [Fact]
    public void NormalizeDeadLetterReason_ShouldFallbackToTransportReject_ForUnknownReason()
    {
        var normalized = MessagingMetrics.NormalizeDeadLetterReason("unmapped_custom_reason");
        normalized.Should().Be(MessagingMetrics.DlqReasonTransportReject);
    }
}

