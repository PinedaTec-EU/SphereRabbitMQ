using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using NUlid;

using RabbitMQ.Client;

namespace SphereRabbitMQ.Tests.Integration.Support;

public sealed class RabbitMqDockerFixture : IAsyncLifetime
{
    private const string DefaultHostName = "localhost";
    private const string DefaultManagementBaseUri = "http://localhost:15672/api/";
    private const string DefaultPassword = "guest";
    private const string DefaultUserName = "guest";
    private const string DefaultVirtualHost = "/";
    private const string TestVirtualHostPrefix = "test-sphere-runtime-it";

    private readonly string _containerName = $"sphere-rabbitmq-tests-{Ulid.NewUlid()}";
    private readonly HttpClient _httpClient = new();
    private string _hostName = DefaultHostName;
    private string _managementBaseUri = DefaultManagementBaseUri;
    private string _password = DefaultPassword;
    private string _bootstrapVirtualHost = DefaultVirtualHost;
    private string _userName = DefaultUserName;
    private string _virtualHost = $"{TestVirtualHostPrefix}-{Ulid.NewUlid()}";
    private bool _usesDockerContainer;

    public int AmqpPort { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (RabbitMqRuntimeIntegrationSettings.TryCreate(out var settings) && settings is not null)
        {
            _hostName = settings.HostName;
            _userName = settings.UserName;
            _password = settings.Password;
            _bootstrapVirtualHost = settings.VirtualHost;
            _managementBaseUri = ResolveManagementBaseUri(_hostName);
            AmqpPort = settings.Port;
            IsAvailable = await WaitForBrokerAsync(_bootstrapVirtualHost);

            if (!IsAvailable)
            {
                throw new InvalidOperationException(
                    $"RabbitMQ integration broker at '{_hostName}:{AmqpPort}' is not reachable. " +
                    "Set SPHERE_RABBITMQ_AMQP_HOST/SPHERE_RABBITMQ_AMQP_PORT explicitly or remove SPHERE_RABBITMQ_* settings to fall back to Docker.");
            }

            await RecreateManagedVirtualHostAsync();
            await ProvisionTopologyAsync();
            return;
        }

        if (!RabbitMqDockerAvailability.IsDockerAvailable())
        {
            throw new InvalidOperationException(
                "Runtime integration tests require either a reachable RabbitMQ configured through SPHERE_RABBITMQ_AMQP_* / SPHERE_RABBITMQ_* or a local Docker daemon.");
        }

        AmqpPort = GetFreePort();
        var managementPort = GetFreePort();

        await RunDockerAsync($"run -d --rm --name {_containerName} -p {AmqpPort}:5672 -p {managementPort}:15672 rabbitmq:3.13-management");
        _usesDockerContainer = true;
        _managementBaseUri = $"http://{_hostName}:{managementPort}/api/";
        IsAvailable = await WaitForBrokerAsync(_bootstrapVirtualHost);

        if (IsAvailable)
        {
            await RecreateManagedVirtualHostAsync();
            await ProvisionTopologyAsync();
            return;
        }

        throw new InvalidOperationException("RabbitMQ Docker container did not become ready in time.");
    }

    public async Task DisposeAsync()
    {
        await DeleteManagedVirtualHostAsync();

        if (!string.IsNullOrWhiteSpace(_containerName))
        {
            await RunDockerAsync($"rm -f {_containerName}", ignoreExitCode: true);
        }

        _httpClient.Dispose();
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
        await channel.QueueBindAsync("orders.created.dlq", "orders.created.dlx", "orders.created", null, false);
    }

    public ConnectionFactory CreateConnectionFactory()
        => new()
        {
            HostName = _hostName,
            Port = AmqpPort,
            UserName = _userName,
            Password = _password,
            VirtualHost = _virtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = false,
            ConsumerDispatchConcurrency = 1,
        };

    public string CreateConnectionString()
        => $"amqp://{_userName}:{_password}@{_hostName}:{AmqpPort}/{Uri.EscapeDataString(_virtualHost)}";

    private async Task RecreateManagedVirtualHostAsync()
    {
        await DeleteManagedVirtualHostAsync();
        await SendManagementRequestAsync(HttpMethod.Put, $"vhosts/{Uri.EscapeDataString(_virtualHost)}");
        await SendManagementRequestAsync(
            HttpMethod.Put,
            $"permissions/{Uri.EscapeDataString(_virtualHost)}/{Uri.EscapeDataString(_userName)}",
            "{\"configure\":\".*\",\"write\":\".*\",\"read\":\".*\"}");
    }

    private async Task DeleteManagedVirtualHostAsync()
    {
        try
        {
            await SendManagementRequestAsync(HttpMethod.Delete, $"vhosts/{Uri.EscapeDataString(_virtualHost)}", allowNotFound: true);
        }
        catch
        {
            if (!_usesDockerContainer)
            {
                throw;
            }
        }
    }

    private async Task SendManagementRequestAsync(HttpMethod method, string relativePath, string? jsonContent = null, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(_managementBaseUri), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_userName}:{_password}")));

        if (jsonContent is not null)
        {
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
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

    private static string ResolveManagementBaseUri(string hostName)
    {
        var configuredManagementUrl = Environment.GetEnvironmentVariable("SPHERE_RABBITMQ_MANAGEMENT_URL");
        return string.IsNullOrWhiteSpace(configuredManagementUrl)
            ? $"http://{hostName}:15672/api/"
            : configuredManagementUrl;
    }

    private async Task<bool> WaitForBrokerAsync(string virtualHost)
    {
        var factory = CreateConnectionFactory(virtualHost);
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

    private ConnectionFactory CreateConnectionFactory(string virtualHost)
        => new()
        {
            HostName = _hostName,
            Port = AmqpPort,
            UserName = _userName,
            Password = _password,
            VirtualHost = virtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = false,
            ConsumerDispatchConcurrency = 1,
        };

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
