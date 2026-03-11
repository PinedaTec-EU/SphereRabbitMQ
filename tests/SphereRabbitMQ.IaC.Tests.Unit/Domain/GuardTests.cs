using System.Reflection;

namespace SphereRabbitMQ.IaC.Tests.Unit.Domain;

public sealed class GuardTests
{
    [Fact]
    public void AgainstNullOrWhiteSpace_ReturnsTrimmedValue()
    {
        var result = InvokeAgainstNullOrWhiteSpace("  sales  ", "name");

        Assert.Equal("sales", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AgainstNullOrWhiteSpace_ThrowsForInvalidValues(string? value)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => InvokeAgainstNullOrWhiteSpace(value, "name"));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void AgainstNullList_Throws_WhenValueIsNull()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => InvokeAgainstNullList<string>(null, "items"));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void AgainstNullDictionary_Throws_WhenValueIsNull()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => InvokeAgainstNullDictionary<string, string>(null, "items"));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void AgainstNullList_ReturnsOriginalInstance_WhenValueIsValid()
    {
        IReadOnlyList<string> items = ["a"];

        var result = InvokeAgainstNullList(items, "items");

        Assert.Same(items, result);
    }

    [Fact]
    public void AgainstNullDictionary_ReturnsOriginalInstance_WhenValueIsValid()
    {
        IReadOnlyDictionary<string, string> items = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a"] = "b",
        };

        var result = InvokeAgainstNullDictionary(items, "items");

        Assert.Same(items, result);
    }

    private static string InvokeAgainstNullOrWhiteSpace(string? value, string paramName)
    {
        var method = GetGuardType().GetMethod("AgainstNullOrWhiteSpace", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [value, paramName])!;
    }

    private static IReadOnlyList<T> InvokeAgainstNullList<T>(IReadOnlyList<T>? value, string paramName)
    {
        var method = GetGuardType()
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == "AgainstNull" && candidate.IsGenericMethodDefinition && candidate.GetGenericArguments().Length == 1)
            .MakeGenericMethod(typeof(T));
        return (IReadOnlyList<T>)method.Invoke(null, [value, paramName])!;
    }

    private static IReadOnlyDictionary<TKey, TValue> InvokeAgainstNullDictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? value, string paramName)
        where TKey : notnull
    {
        var method = GetGuardType()
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == "AgainstNull" && candidate.IsGenericMethodDefinition && candidate.GetGenericArguments().Length == 2)
            .MakeGenericMethod(typeof(TKey), typeof(TValue));
        return (IReadOnlyDictionary<TKey, TValue>)method.Invoke(null, [value, paramName])!;
    }

    private static Type GetGuardType()
        => typeof(SphereRabbitMQ.IaC.Domain.Topology.TopologyDefinition).Assembly
            .GetType("SphereRabbitMQ.IaC.Domain.Internal.Guard", throwOnError: true)!;
}
