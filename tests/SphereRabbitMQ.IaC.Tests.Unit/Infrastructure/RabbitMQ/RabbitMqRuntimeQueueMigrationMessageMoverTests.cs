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
    public void CreateServiceProvider_UsesBaseUriHost_WhenAmqpHostNameIsMissing()
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

        using var provider = CreateServiceProvider(options, "tenant-a");
        var runtimeOptions = provider.GetRequiredService<IOptions<SphereRabbitMqOptions>>().Value;

        Assert.Equal("rabbit.internal", runtimeOptions.HostName);
        Assert.Equal(5673, runtimeOptions.Port);
        Assert.Equal("tenant-a", runtimeOptions.VirtualHost);
        Assert.Equal("guest", runtimeOptions.UserName);
        Assert.Equal("guest", runtimeOptions.Password);
        Assert.False(runtimeOptions.ValidateTopologyOnStartup);
        Assert.Equal("SphereRabbitMQ.IaC.Migrations", runtimeOptions.ClientProvidedName);
    }

    [Fact]
    public void CreateServiceProvider_UsesAmqpHostNameOverride_WhenProvided()
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

        using var provider = CreateServiceProvider(options, "tenant-b");
        var runtimeOptions = provider.GetRequiredService<IOptions<SphereRabbitMqOptions>>().Value;

        Assert.Equal("override-host", runtimeOptions.HostName);
        Assert.Equal("tenant-b", runtimeOptions.VirtualHost);
    }

    private static ServiceProvider CreateServiceProvider(RabbitMqManagementOptions options, string virtualHostName)
    {
        var mover = new RabbitMqRuntimeQueueMigrationMessageMover(options);
        var factoryMethod = typeof(RabbitMqRuntimeQueueMigrationMessageMover)
            .GetMethod("CreateServiceProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(factoryMethod);

        return (ServiceProvider)factoryMethod!.Invoke(mover, [virtualHostName])!;
    }
}
