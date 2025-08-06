using System.Diagnostics;
using Xunit;

namespace TicketHub.TestSupport;

/// <summary>Skips Testcontainers tests on machines without a Docker daemon instead of failing them.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Available = new(Probe);

    public DockerFactAttribute()
    {
        if (!Available.Value) Skip = "Docker is not available";
    }

    private static bool Probe()
    {
        if (Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS") == "1") return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
