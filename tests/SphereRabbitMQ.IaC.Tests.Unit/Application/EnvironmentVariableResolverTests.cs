using SphereRabbitMQ.IaC.Application.Services;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application;

public sealed class EnvironmentVariableResolverTests
{
    [Fact]
    public void Resolve_UsesExplicitVariables_BeforeEnvironment()
    {
        var resolver = new EnvironmentVariableResolver();
        Environment.SetEnvironmentVariable("SPHERE_REGION", "eu-west");

        var result = resolver.Resolve(
            "topology-${SPHERE_REGION}",
            new Dictionary<string, string?> { ["SPHERE_REGION"] = "us-east" });

        Assert.Equal("topology-us-east", result);
    }

    [Fact]
    public void Resolve_Throws_WhenVariableIsMissing()
    {
        var resolver = new EnvironmentVariableResolver();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve("topology-${MISSING_VAR}", new Dictionary<string, string?>()));

        Assert.Contains("MISSING_VAR", exception.Message);
    }
}
