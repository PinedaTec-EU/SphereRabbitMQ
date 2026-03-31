using Microsoft.Extensions.DependencyInjection;
using SphereRabbitMQ.IaC.Cli.Commands;
using SphereRabbitMQ.IaC.Cli.DependencyInjection;

namespace SphereRabbitMQ.IaC.Cli;

/// <summary>
/// Application entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var services = new ServiceCollection()
            .AddTopologyCli()
            .BuildServiceProvider();

        var rootCommand = TopologyRootCommandFactory.Create(services);
        return rootCommand.Parse(args).InvokeAsync();
    }
}
