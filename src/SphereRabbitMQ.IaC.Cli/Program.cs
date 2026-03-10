namespace SphereRabbitMQ.IaC.Cli;

/// <summary>
/// Phase 2 keeps the CLI entry point minimal until command composition is introduced.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return 0;
    }
}
