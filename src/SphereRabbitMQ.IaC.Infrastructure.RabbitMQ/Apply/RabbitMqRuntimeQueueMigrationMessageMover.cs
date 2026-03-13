using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SphereRabbitMQ.DependencyInjection;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;
using SphereRabbitMQ.Infrastructure.RabbitMQ.Migration.Interfaces;

namespace SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;

public sealed class RabbitMqRuntimeQueueMigrationMessageMover : IQueueMigrationMessageMover, IAsyncDisposable
{
    private readonly RabbitMqManagementOptions _options;

    public RabbitMqRuntimeQueueMigrationMessageMover(RabbitMqManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async ValueTask MoveAsync(
        string virtualHostName,
        string sourceQueueName,
        string destinationQueueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualHostName);

        await using var serviceProvider = CreateServiceProvider(virtualHostName);
        var queueMessageMover = serviceProvider.GetRequiredService<IQueueMessageMover>();
        await queueMessageMover.MoveAsync(sourceQueueName, destinationQueueName, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await ValueTask.CompletedTask;
    }

    private ServiceProvider CreateServiceProvider(string virtualHostName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSphereRabbitMq(runtimeOptions =>
        {
            runtimeOptions.HostName = string.IsNullOrWhiteSpace(_options.AmqpHostName) ? _options.BaseUri.Host : _options.AmqpHostName;
            runtimeOptions.Port = _options.AmqpPort;
            runtimeOptions.VirtualHost = virtualHostName;
            runtimeOptions.UserName = _options.Username;
            runtimeOptions.Password = _options.Password;
            runtimeOptions.ValidateTopologyOnStartup = false;
            runtimeOptions.ClientProvidedName = "SphereRabbitMQ.IaC.Migrations";
        });

        return services.BuildServiceProvider();
    }
}
