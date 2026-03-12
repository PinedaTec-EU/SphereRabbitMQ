using System.Diagnostics;
using System.Net.Sockets;
using RabbitMQ.Client;

namespace SphereRabbitMQ.Tests.Integration.Support;

public sealed class RabbitMqDockerFixture : IAsyncLifetime
{
    private const string DefaultHostName = "localhost";
    private const string DefaultPassword = "guest";
    private const string DefaultUserName = "guest";
    private const string DefaultVirtualHost = "/";

    private readonly string _containerName = $"sphere-rabbitmq-tests-{Guid.NewGuid():N}";

    public int AmqpPort { get; private set; }

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        if (!await CanUseDockerAsync())
        {
            return;
        }

        AmqpPort = GetFreePort();
        var managementPort = GetFreePort();

        await RunDockerAsync($"run -d --rm --name {_containerName} -p {AmqpPort}:5672 -p {managementPort}:15672 rabbitmq:3.13-management");
        IsAvailable = await WaitForBrokerAsync();

        if (IsAvailable)
        {
            await ProvisionTopologyAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_containerName))
        {
            await RunDockerAsync($"rm -f {_containerName}", ignoreExitCode: true);
        }
    }

    private async Task ProvisionTopologyAsync()
    {
        var factory = CreateConnectionFactory();
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync("orders", "topic", true, false, null, false, false);
        await channel.QueueDeclareAsync("orders.created", true, false, false, null, false, false);
        await channel.QueueDeclareAsync("orders.created.high", true, false, false, null, false, false);
        await channel.QueueDeclareAsync("orders.created.low", true, false, false, null, false, false);
        await channel.QueueDeclareAsync("orders.created.multi", true, false, false, null, false, false);
        await channel.QueueDeclareAsync("orders.created.migration", true, false, false, null, false, false);
        await channel.QueueBindAsync("orders.created", "orders", "orders.created", null, false);
        await channel.QueueBindAsync("orders.created.high", "orders", "orders.created.high", null, false);
        await channel.QueueBindAsync("orders.created.low", "orders", "orders.created.low", null, false);
        await channel.QueueBindAsync("orders.created.multi", "orders", "orders.created.eu", null, false);
        await channel.QueueBindAsync("orders.created.multi", "orders", "orders.created.us", null, false);

        await channel.ExchangeDeclareAsync("orders.created.retry", "direct", true, false, null, false, false);
        await channel.QueueDeclareAsync(
            "orders.created.retry.step1",
            true,
            false,
            false,
            new Dictionary<string, object?>
            {
                ["x-message-ttl"] = 250,
                ["x-dead-letter-exchange"] = "orders",
                ["x-dead-letter-routing-key"] = "orders.created",
            },
            false,
            false);
        await channel.QueueBindAsync("orders.created.retry.step1", "orders.created.retry", "orders.created.retry.step1", null, false);

        await channel.ExchangeDeclareAsync("orders.created.dlx", "direct", true, false, null, false, false);
        await channel.QueueDeclareAsync("orders.created.dlq", true, false, false, null, false, false);
        await channel.QueueBindAsync("orders.created.dlq", "orders.created.dlx", "orders.created.dlq", null, false);
    }

    public ConnectionFactory CreateConnectionFactory()
        => new()
        {
            HostName = DefaultHostName,
            Port = AmqpPort,
            UserName = DefaultUserName,
            Password = DefaultPassword,
            VirtualHost = DefaultVirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = false,
            ConsumerDispatchConcurrency = 1,
        };

    public string CreateConnectionString()
        => $"amqp://{DefaultUserName}:{DefaultPassword}@{DefaultHostName}:{AmqpPort}/{Uri.EscapeDataString(DefaultVirtualHost)}";

    private async Task<bool> WaitForBrokerAsync()
    {
        var factory = CreateConnectionFactory();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync();
                return connection.IsOpen;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        return false;
    }

    private static async Task<bool> CanUseDockerAsync()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo("docker", "version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunDockerAsync(string arguments, bool ignoreExitCode = false)
    {
        var process = Process.Start(new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Unable to start docker process.");

        await process.WaitForExitAsync();
        if (!ignoreExitCode && process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Docker command failed: docker {arguments}. {error}");
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
