using SphereRabbitMQ.Domain.Topology;

namespace SphereRabbitMQ.Abstractions.Configuration;

public sealed record SphereRabbitMqOptions
{
    public string? ConnectionString { get; private set; }

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string VirtualHost { get; set; } = "/";

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string ClientProvidedName { get; set; } = "SphereRabbitMQ";

    public bool EnablePublisherConfirms { get; set; } = true;

    public ushort PublisherChannelPoolSize { get; set; } = 4;

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool ValidateTopologyOnStartup { get; set; }

    public TopologyExpectation ExpectedTopology { get; set; } = new(Array.Empty<string>(), Array.Empty<string>());

    public SphereRabbitMqOptions SetConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var uri = new Uri(connectionString, UriKind.Absolute);
        var userInfoSegments = uri.UserInfo.Split(':', 2, StringSplitOptions.None);

        ConnectionString = connectionString;
        HostName = uri.Host;
        Port = uri.IsDefaultPort
            ? string.Equals(uri.Scheme, "amqps", StringComparison.OrdinalIgnoreCase) ? 5671 : 5672
            : uri.Port;
        VirtualHost = string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? "/"
            : Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        UserName = userInfoSegments.Length > 0 ? Uri.UnescapeDataString(userInfoSegments[0]) : UserName;
        Password = userInfoSegments.Length > 1 ? Uri.UnescapeDataString(userInfoSegments[1]) : Password;
        return this;
    }
}
