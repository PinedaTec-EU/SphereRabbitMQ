using SphereRabbitMQ.IaC.Domain.Internal;

namespace SphereRabbitMQ.IaC.Domain.Topology;

/// <summary>
/// Defines deterministic naming rules for generated topology artifacts.
/// </summary>
public sealed record NamingConventionPolicy
{
    /// <summary>
    /// Default naming policy used when a topology does not provide an override.
    /// </summary>
    public static NamingConventionPolicy Default { get; } = new();

    public string Separator { get; init; } = ".";

    public string RetryExchangeSuffix { get; init; } = "retry";

    public string RetryQueueSuffix { get; init; } = "retry";

    public string DeadLetterExchangeSuffix { get; init; } = "dlx";

    public string DeadLetterQueueSuffix { get; init; } = "dlq";

    public string StepTokenPrefix { get; init; } = "step";

    /// <summary>
    /// Returns a deterministic retry queue name for the specified queue and retry step.
    /// </summary>
    public string GetRetryQueueName(string queueName, string stepName)
        => Join(queueName, RetryQueueSuffix, stepName);

    /// <summary>
    /// Returns a deterministic retry exchange name for the specified queue.
    /// </summary>
    public string GetRetryExchangeName(string queueName)
        => Join(queueName, RetryExchangeSuffix);

    /// <summary>
    /// Returns a deterministic dead-letter exchange name for the specified queue.
    /// </summary>
    public string GetDeadLetterExchangeName(string queueName)
        => Join(queueName, DeadLetterExchangeSuffix);

    /// <summary>
    /// Returns a deterministic dead-letter queue name for the specified queue.
    /// </summary>
    public string GetDeadLetterQueueName(string queueName)
        => Join(queueName, DeadLetterQueueSuffix);

    /// <summary>
    /// Returns a deterministic retry step token.
    /// </summary>
    public string GetRetryStepToken(int index)
        => $"{StepTokenPrefix}{index + 1}";

    /// <summary>
    /// Determines whether an exchange name belongs to the secondary retry/dead-letter naming flow.
    /// </summary>
    public bool IsSecondaryExchangeName(string exchangeName)
    {
        var separator = Guard.AgainstNullOrWhiteSpace(Separator, nameof(Separator));
        var deadLetterSuffix = $"{separator}{Guard.AgainstNullOrWhiteSpace(DeadLetterExchangeSuffix, nameof(DeadLetterExchangeSuffix))}";
        var retrySuffix = $"{separator}{Guard.AgainstNullOrWhiteSpace(RetryExchangeSuffix, nameof(RetryExchangeSuffix))}";

        return exchangeName.EndsWith(deadLetterSuffix, StringComparison.Ordinal) ||
               exchangeName.EndsWith(retrySuffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a queue name belongs to the secondary retry/dead-letter naming flow.
    /// </summary>
    public bool IsSecondaryQueueName(string queueName)
    {
        var separator = Guard.AgainstNullOrWhiteSpace(Separator, nameof(Separator));
        var deadLetterSuffix = $"{separator}{Guard.AgainstNullOrWhiteSpace(DeadLetterQueueSuffix, nameof(DeadLetterQueueSuffix))}";
        var retryToken = $"{separator}{Guard.AgainstNullOrWhiteSpace(RetryQueueSuffix, nameof(RetryQueueSuffix))}{separator}";

        return queueName.EndsWith(deadLetterSuffix, StringComparison.Ordinal) ||
               queueName.Contains(retryToken, StringComparison.Ordinal);
    }

    private string Join(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var sanitized = segments
            .Select(segment => Guard.AgainstNullOrWhiteSpace(segment, nameof(segments)))
            .ToArray();

        return string.Join(Guard.AgainstNullOrWhiteSpace(Separator, nameof(Separator)), sanitized);
    }
}
