using System.Diagnostics;
using System.Text;

namespace MediaToolsNext.Infrastructure;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

internal sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        // stdout and stderr handlers fire on arbitrary threadpool threads;
        // StringBuilder is not thread-safe so we must lock before appending.
        var stdoutLock = new object();
        var stderrLock = new object();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stdoutLock) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stderrLock) stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessResult(
                process.ExitCode,
                stdout.ToString().Trim(),
                stderr.ToString().Trim(),
                false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, stdout.ToString().Trim(), stderr.ToString().Trim(), true);
        }
    }
}
