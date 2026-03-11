using SphereRabbitMQ.Domain.Topology;

namespace SphereRabbitMQ.Abstractions.Configuration;

public sealed record SphereRabbitMqOptions
{
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
}
