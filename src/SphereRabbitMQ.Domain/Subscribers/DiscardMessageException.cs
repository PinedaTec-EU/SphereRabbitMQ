using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.Domain.Subscribers;

[ExcludeFromCodeCoverage]
public sealed class DiscardMessageException : Exception
{
    public DiscardMessageException(string message)
        : base(message)
    {
    }

    public DiscardMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
