namespace Syed.Messaging;

/// <summary>
/// Transport-neutral IMessageBus implementation that creates consistent message envelopes.
/// </summary>
public class TransportMessageBus : IMessageBus
{
    private const string MessageIdHeader = "message-id";
    private const string CorrelationIdHeader = "correlation-id";
    private const string MessageTypeHeader = "message-type";
    private const string MessageVersionHeader = "message-version";

    private readonly IMessageTransport _transport;
    private readonly ISerializer _serializer;
    private readonly IMessageTypeRegistry _messageTypeRegistry;

    public TransportMessageBus(
        IMessageTransport transport,
        ISerializer serializer,
        IMessageTypeRegistry messageTypeRegistry)
    {
        _transport = transport;
        _serializer = serializer;
        _messageTypeRegistry = messageTypeRegistry;
    }

    public Task PublishAsync<T>(string destination, T message, CancellationToken ct = default)
    {
        var envelope = CreateEnvelope(typeof(T), _serializer.Serialize(message));
        return _transport.PublishAsync(envelope, destination, ct);
    }

    public Task SendAsync<T>(string destination, T message, CancellationToken ct = default)
        => PublishAsync(destination, message, ct);

    public Task PublishRawAsync(
        string destination,
        byte[] payload,
        string? messageType = null,
        Dictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        var envelope = CreateRawEnvelope(payload, messageType, headers);
        return _transport.PublishAsync(envelope, destination, ct);
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        string destination,
        TRequest message,
        CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid().ToString();
        var envelope = CreateEnvelope(typeof(TRequest), _serializer.Serialize(message), correlationId);

        var responseEnvelope = await _transport.RequestAsync(envelope, destination, ct);
        return _serializer.Deserialize<TResponse>(responseEnvelope.Body);
    }

    private MessageEnvelope CreateEnvelope(Type messageType, byte[] body, string? correlationId = null)
    {
        var (typeKey, version) = GetTypeMetadata(messageType);
        var messageId = Guid.NewGuid().ToString();
        correlationId ??= Guid.NewGuid().ToString();

        var headers = CreateHeaders(messageId, correlationId, typeKey, version);

        return new MessageEnvelope
        {
            MessageType = typeKey,
            MessageVersion = version,
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = headers,
            Body = body
        };
    }

    private MessageEnvelope CreateRawEnvelope(
        byte[] payload,
        string? messageType,
        Dictionary<string, string>? headers)
    {
        var typeKey = string.IsNullOrWhiteSpace(messageType) ? "raw" : messageType;
        var allHeaders = headers is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(headers);

        var messageId = allHeaders.TryGetValue(MessageIdHeader, out var existingMessageId) && !string.IsNullOrWhiteSpace(existingMessageId)
            ? existingMessageId
            : Guid.NewGuid().ToString();

        var correlationId = allHeaders.TryGetValue(CorrelationIdHeader, out var existingCorrelationId) && !string.IsNullOrWhiteSpace(existingCorrelationId)
            ? existingCorrelationId
            : Guid.NewGuid().ToString();

        allHeaders[MessageIdHeader] = messageId;
        allHeaders[CorrelationIdHeader] = correlationId;
        allHeaders[MessageTypeHeader] = typeKey!;
        allHeaders.TryGetValue(MessageVersionHeader, out var messageVersion);

        return new MessageEnvelope
        {
            MessageType = typeKey!,
            MessageVersion = messageVersion,
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = allHeaders,
            Body = payload
        };
    }

    private (string TypeKey, string? Version) GetTypeMetadata(Type messageType)
    {
        if (_messageTypeRegistry.TryGetTypeKey(messageType, out var typeKey, out var version))
        {
            return (typeKey!, version);
        }

        return (messageType.FullName ?? messageType.Name, null);
    }

    private static Dictionary<string, string> CreateHeaders(
        string messageId,
        string correlationId,
        string messageType,
        string? messageVersion)
    {
        var headers = new Dictionary<string, string>
        {
            [MessageIdHeader] = messageId,
            [CorrelationIdHeader] = correlationId,
            [MessageTypeHeader] = messageType
        };

        if (!string.IsNullOrWhiteSpace(messageVersion))
        {
            headers[MessageVersionHeader] = messageVersion;
        }

        return headers;
    }
}
