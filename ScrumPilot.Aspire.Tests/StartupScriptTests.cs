using System.Diagnostics;
using Xunit;

namespace ScrumPilot.Aspire.Tests;

public sealed class StartupScriptTests
{
    [Fact]
    public async Task SqlHelperRestoresCallerEnvironmentOnSuccessAndFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                ArgumentList =
                {
                    "-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "Scripts", "Test-StartupEnvironment.ps1")
                }
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }
}
