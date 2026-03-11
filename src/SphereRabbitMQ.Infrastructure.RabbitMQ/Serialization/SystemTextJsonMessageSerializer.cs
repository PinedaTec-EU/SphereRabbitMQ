using System.Text.Json;
using SphereRabbitMQ.Abstractions.Serialization;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Serialization;

public sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _serializerOptions;

    public SystemTextJsonMessageSerializer(JsonSerializerOptions? serializerOptions = null)
    {
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string ContentType => "application/json";

    public TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> body)
        => JsonSerializer.Deserialize<TMessage>(body.Span, _serializerOptions)
           ?? throw new InvalidOperationException($"Unable to deserialize message body to '{typeof(TMessage).Name}'.");

    public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
        => JsonSerializer.SerializeToUtf8Bytes(message, _serializerOptions);
}
