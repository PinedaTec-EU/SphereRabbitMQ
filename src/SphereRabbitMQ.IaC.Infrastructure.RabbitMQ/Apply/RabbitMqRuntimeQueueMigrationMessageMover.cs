using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

public sealed class RabbitMqRuntimeQueueMigrationMessageMover : IQueueMigrationMessageMover, IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IQueueMessageMover _queueMessageMover;

    public RabbitMqRuntimeQueueMigrationMessageMover(RabbitMqManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSphereRabbitMq(runtimeOptions =>
        {
            runtimeOptions.HostName = string.IsNullOrWhiteSpace(options.AmqpHostName) ? options.BaseUri.Host : options.AmqpHostName;
            runtimeOptions.Port = options.AmqpPort;
            runtimeOptions.VirtualHost = options.AmqpVirtualHost;
            runtimeOptions.UserName = options.Username;
            runtimeOptions.Password = options.Password;
            runtimeOptions.ValidateTopologyOnStartup = false;
            runtimeOptions.ClientProvidedName = "SphereRabbitMQ.IaC.Migrations";
        });

        _serviceProvider = services.BuildServiceProvider();
        _queueMessageMover = _serviceProvider.GetRequiredService<IQueueMessageMover>();
    }

    public async ValueTask MoveAsync(
        string sourceQueueName,
        string destinationQueueName,
        CancellationToken cancellationToken = default)
    {
        await _queueMessageMover.MoveAsync(sourceQueueName, destinationQueueName, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
    }
}
