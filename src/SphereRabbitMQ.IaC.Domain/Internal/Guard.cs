namespace SphereRabbitMQ.IaC.Domain.Internal;

internal static class Guard
{
    public static string AgainstNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", paramName);
        }

        return value.Trim();
    }

    public static IReadOnlyList<T> AgainstNull<T>(IReadOnlyList<T>? value, string paramName)
    {
        return value ?? throw new ArgumentNullException(paramName);
    }

    public static IReadOnlyDictionary<TKey, TValue> AgainstNull<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? value,
        string paramName) where TKey : notnull
    {
        return value ?? throw new ArgumentNullException(paramName);
    }
}
