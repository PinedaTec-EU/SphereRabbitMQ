namespace SphereRabbitMQ.Tests.Integration.EnvironmentFile;

internal static class IntegrationEnvironmentLoader
{
    private static readonly object Sync = new();
    private static bool _loaded;

    internal static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            var repositoryRoot = ResolveRepositoryRoot();
            var environmentFilePath = Path.Combine(repositoryRoot, ".vscode", ".env");
            if (!File.Exists(environmentFilePath))
            {
                _loaded = true;
                return;
            }

            foreach (var line in File.ReadAllLines(environmentFilePath))
            {
                ApplyLine(line);
            }

            _loaded = true;
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "SphereRabbitMQ.slnx")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for integration test environment loading.");
    }

    private static void ApplyLine(string line)
    {
        var trimmedLine = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
        {
            return;
        }

        var separatorIndex = trimmedLine.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return;
        }

        var key = trimmedLine[..separatorIndex].Trim();
        var rawValue = trimmedLine[(separatorIndex + 1)..].Trim();
        var value = Unquote(rawValue);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }
}
