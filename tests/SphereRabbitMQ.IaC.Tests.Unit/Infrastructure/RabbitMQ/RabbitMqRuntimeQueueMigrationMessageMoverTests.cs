using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SphereRabbitMQ.Abstractions.Configuration;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Apply;
using SphereRabbitMQ.IaC.Infrastructure.RabbitMQ.Configuration;

namespace SphereRabbitMQ.IaC.Tests.Unit.Infrastructure.RabbitMQ;

public sealed class RabbitMqRuntimeQueueMigrationMessageMoverTests
{
    [Fact]
    public void Constructor_Throws_WhenOptionsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RabbitMqRuntimeQueueMigrationMessageMover(null!));
    }

    [Fact]
    public async Task Constructor_UsesBaseUriHost_WhenAmqpHostNameIsMissing()
    {
        var options = new RabbitMqManagementOptions
        {
            BaseUri = new Uri("http://rabbit.internal:15672/api/"),
            Username = "guest",
            Password = "guest",
            AmqpHostName = " ",
            AmqpPort = 5673,
            AmqpVirtualHost = "sales",
        };

        await using var mover = new RabbitMqRuntimeQueueMigrationMessageMover(options);
        var runtimeOptions = ResolveRuntimeOptions(mover);

        Assert.Equal("rabbit.internal", runtimeOptions.HostName);
        Assert.Equal(5673, runtimeOptions.Port);
        Assert.Equal("sales", runtimeOptions.VirtualHost);
        Assert.Equal("guest", runtimeOptions.UserName);
        Assert.Equal("guest", runtimeOptions.Password);
        Assert.False(runtimeOptions.ValidateTopologyOnStartup);
        Assert.Equal("SphereRabbitMQ.IaC.Migrations", runtimeOptions.ClientProvidedName);
    }

    [Fact]
    public async Task Constructor_UsesAmqpHostNameOverride_WhenProvided()
    {
        var options = new RabbitMqManagementOptions
        {
            BaseUri = new Uri("http://rabbit.internal:15672/api/"),
            Username = "guest",
            Password = "guest",
            AmqpHostName = "override-host",
            AmqpPort = 5672,
            AmqpVirtualHost = "/",
        };

        await using var mover = new RabbitMqRuntimeQueueMigrationMessageMover(options);
        var runtimeOptions = ResolveRuntimeOptions(mover);

        Assert.Equal("override-host", runtimeOptions.HostName);
    }

    private static SphereRabbitMqOptions ResolveRuntimeOptions(RabbitMqRuntimeQueueMigrationMessageMover mover)
    {
        var providerField = typeof(RabbitMqRuntimeQueueMigrationMessageMover)
            .GetField("_serviceProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(providerField);

        var provider = (ServiceProvider)providerField!.GetValue(mover)!;
        return provider.GetRequiredService<IOptions<SphereRabbitMqOptions>>().Value;
    }
}
