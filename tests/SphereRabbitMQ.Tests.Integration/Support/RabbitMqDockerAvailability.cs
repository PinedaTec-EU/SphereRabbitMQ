using System.Diagnostics;

namespace SphereRabbitMQ.Tests.Integration.Support;

internal static class RabbitMqDockerAvailability
{
    internal const string DockerRequiredMessage = "Integration test requires Docker with an accessible daemon (`docker version`).";

    internal static bool IsDockerAvailable()
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

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
