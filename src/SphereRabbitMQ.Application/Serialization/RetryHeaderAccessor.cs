using SphereRabbitMQ.Domain.Retry;

namespace SphereRabbitMQ.Application.Serialization;

public sealed class RetryHeaderAccessor
{
    public const string RetryCountHeaderName = "x-retry-count";

    public RetryMetadata Read(IReadOnlyDictionary<string, object?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (!headers.TryGetValue(RetryCountHeaderName, out var value) || value is null)
        {
            return RetryMetadata.None;
        }

        return int.TryParse(value.ToString(), out var retryCount)
            ? new RetryMetadata(retryCount)
            : RetryMetadata.None;
    }

    public IReadOnlyDictionary<string, object?> Write(IReadOnlyDictionary<string, object?> headers, int retryCount)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var updatedHeaders = new Dictionary<string, object?>(headers, StringComparer.Ordinal)
        {
            [RetryCountHeaderName] = retryCount,
        };

        return updatedHeaders;
    }
}
