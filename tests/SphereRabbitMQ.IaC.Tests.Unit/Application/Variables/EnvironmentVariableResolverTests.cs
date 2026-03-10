using SphereRabbitMQ.IaC.Application.Variables;
using SphereRabbitMQ.IaC.Application.Variables.Interfaces;

namespace SphereRabbitMQ.IaC.Tests.Unit.Application.Variables;

public sealed class EnvironmentVariableResolverTests
{
    [Fact]
    public void Resolve_UsesExplicitVariables_BeforeEnvironment()
    {
        IVariableResolver variableResolver = new EnvironmentVariableResolver();
        Environment.SetEnvironmentVariable("SPHERE_REGION", "eu-west");

        var resolvedValue = variableResolver.Resolve(
            "topology-${SPHERE_REGION}",
            new Dictionary<string, string?> { ["SPHERE_REGION"] = "us-east" });

        Assert.Equal("topology-us-east", resolvedValue);
    }

    [Fact]
    public void Resolve_Throws_WhenVariableIsMissing()
    {
        IVariableResolver variableResolver = new EnvironmentVariableResolver();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            variableResolver.Resolve("topology-${MISSING_VAR}", new Dictionary<string, string?>()));

        Assert.Contains("MISSING_VAR", exception.Message);
    }
}
