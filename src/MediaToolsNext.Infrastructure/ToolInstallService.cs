using System.Diagnostics;

namespace MediaToolsNext.Infrastructure;

public sealed record ToolInstallResult(bool Success, string Command, string Output);

public interface IToolInstallProcessRunner
{
    Task<ToolInstallResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class ToolInstallService
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyDictionary<string, (string Choco, string Winget)> Packages =
        new Dictionary<string, (string Choco, string Winget)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = ("ffmpeg", "Gyan.FFmpeg"),
            ["ffprobe"] = ("ffmpeg", "Gyan.FFmpeg"),
            ["magick"] = ("imagemagick", "ImageMagick.ImageMagick"),
            ["qpdf"] = ("qpdf", "QPDF.QPDF")
        };

    private readonly IToolInstallProcessRunner _runner;
    private readonly TimeSpan _commandTimeout;

    public ToolInstallService(IToolInstallProcessRunner? runner = null, TimeSpan? commandTimeout = null)
    {
        _runner = runner ?? new ProcessToolInstallRunner();
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
    }

    public async Task<ToolInstallResult> InstallAsync(string toolName, CancellationToken cancellationToken)
    {
        if (!Packages.TryGetValue(toolName, out var package))
            return new(false, toolName, "No installer package mapping is configured for this tool.");

        var choco = await TryRunAsync("choco", ["install", package.Choco, "-y", "--no-progress"], cancellationToken);
        if (choco.Success)
            return choco;

        var winget = await TryRunAsync("winget", ["install", "--id", package.Winget, "--silent", "--accept-source-agreements", "--accept-package-agreements"], cancellationToken);
        return winget.Success
            ? winget
            : new(false, $"{choco.Command}; {winget.Command}", choco.Output + Environment.NewLine + winget.Output);
    }

    public async Task<ToolInstallResult> UpgradeAsync(string toolName, CancellationToken cancellationToken)
    {
        if (!Packages.TryGetValue(toolName, out var package))
            return new(false, toolName, "No installer package mapping is configured for this tool.");

        var choco = await TryRunAsync("choco", ["upgrade", package.Choco, "-y", "--no-progress"], cancellationToken);
        if (choco.Success)
            return choco;

        var winget = await TryRunAsync("winget", ["upgrade", "--id", package.Winget, "--silent", "--accept-source-agreements", "--accept-package-agreements"], cancellationToken);
        return winget.Success
            ? winget
            : new(false, $"{choco.Command}; {winget.Command}", choco.Output + Environment.NewLine + winget.Output);
    }

    public async Task<ToolInstallResult> UpgradeAllAsync(IEnumerable<string> toolNames, CancellationToken cancellationToken)
    {
        var results = new List<ToolInstallResult>();
        foreach (var toolName in toolNames.Distinct(StringComparer.OrdinalIgnoreCase))
            results.Add(await UpgradeAsync(toolName, cancellationToken));

        var success = results.All(result => result.Success);
        return new(success, "upgrade all", string.Join(Environment.NewLine + Environment.NewLine, results.Select(result => $"{result.Command}: {result.Output}")));
    }

    private async Task<ToolInstallResult> TryRunAsync(string fileName, string[] arguments, CancellationToken cancellationToken) =>
        await _runner.RunAsync(fileName, arguments, _commandTimeout, cancellationToken);
}

public sealed class ProcessToolInstallRunner : IToolInstallProcessRunner
{
    public async Task<ToolInstallResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var commandText = fileName + " " + string.Join(" ", arguments);
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(process);
                return new(false, commandText, $"Timed out after {timeout.TotalMinutes:N0} minutes.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new(process.ExitCode == 0, commandText, (stdout + stderr).Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, commandText, ex.Message);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { }
    }
}
