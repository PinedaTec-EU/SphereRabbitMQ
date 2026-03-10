namespace SphereRabbitMQ.IaC.Cli.Commands;

internal static class CommandExitCodes
{
    internal const int Success = 0;
    internal const int ValidationFailed = 1;
    internal const int UnsupportedPlan = 2;
    internal const int ExecutionFailed = 3;
    internal const int DestructivePermissionRequired = 4;
}
