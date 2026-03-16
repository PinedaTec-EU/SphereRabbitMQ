using RabbitMQ.Client;

namespace SphereRabbitMQ.Infrastructure.RabbitMQ.Publishing;

internal sealed class RabbitMqChannelLease : IAsyncDisposable
{
    private readonly Func<IChannel, ValueTask> _returnAction;

    public RabbitMqChannelLease(IChannel channel, Func<IChannel, ValueTask> returnAction)
    {
        Channel = channel;
        _returnAction = returnAction;
    }

    public IChannel Channel { get; }

    public ValueTask DisposeAsync() => _returnAction(Channel);
}
