using System.Diagnostics.CodeAnalysis;

namespace SphereRabbitMQ.Domain.Subscribers;

[ExcludeFromCodeCoverage]
public sealed class NonRetriableMessageException : Exception
{
    public NonRetriableMessageException(string message)
        : base(message)
    {
    }

    public NonRetriableMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
