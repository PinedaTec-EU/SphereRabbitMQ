namespace SphereRabbitMQ.Abstractions.Serialization;

public interface IMessageSerializer
{
    ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message);

    TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> body);

    string ContentType { get; }
}
