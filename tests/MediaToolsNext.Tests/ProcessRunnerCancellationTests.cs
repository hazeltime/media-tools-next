using MediaToolsNext.Infrastructure;
using System.Diagnostics;

namespace MediaToolsNext.Tests;

public class ProcessRunnerCancellationTests
{
    [Fact]
    public async Task CancelledRunKillsChildProcessTree()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var runner = new ProcessRunner();
        var tempDir = Path.Combine(Path.GetTempPath(), "media-tools-next-process-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var childPidPath = Path.Combine(tempDir, "child.pid");
        var scriptPath = Path.Combine(tempDir, "spawn-child.ps1");
        await File.WriteAllTextAsync(scriptPath, $$"""
$child = Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60')
Set-Content -Path '{{childPidPath}}' -Value $child.Id
while ($true) { Start-Sleep -Seconds 1 }
""");

        using var cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var runTask = runner.RunAsync(
                "powershell.exe",
                new[]
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath
                },
                TimeSpan.FromMinutes(5),
                cancellationTokenSource.Token);

            var childPid = await WaitForChildPidAsync(childPidPath);
            cancellationTokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            await Task.Delay(500);

            Assert.False(IsProcessRunning(childPid));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static async Task<int> WaitForChildPidAsync(string childPidPath)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(childPidPath))
            {
                var text = await File.ReadAllTextAsync(childPidPath);
                if (int.TryParse(text.Trim(), out var childPid))
                    return childPid;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for child process id.");
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
