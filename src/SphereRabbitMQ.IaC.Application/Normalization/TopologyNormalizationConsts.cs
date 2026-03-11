namespace SphereRabbitMQ.IaC.Application.Normalization;

internal static class TopologyNormalizationConsts
{
    internal const string DebugQueueRoutingKey = "#";
    internal const string DebugQueueSuffix = "debug";
    internal const string DeadLetterExchangeArgument = "x-dead-letter-exchange";
    internal const string DeadLetterRoutingKeyArgument = "x-dead-letter-routing-key";
    internal const string MessageTtlArgument = "x-message-ttl";
    internal const string QueueTypeArgument = "x-queue-type";
    internal const string GeneratedMetadataKey = "generated-by";
    internal const string GeneratedMetadataValue = "SphereRabbitMQ.IaC";
    internal const string SourceExchangeMetadataKey = "source-exchange";
    internal const string SourceQueueMetadataKey = "source-queue";
    internal const string EmptyExchangeName = "";
}
