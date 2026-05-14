using System.Diagnostics;

namespace MediaToolsNext.Desktop;

public sealed record ToolInstallResult(bool Success, string Command, string Output);

public sealed class ToolInstallService
{
    private static readonly IReadOnlyDictionary<string, (string Choco, string Winget)> Packages =
        new Dictionary<string, (string Choco, string Winget)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = ("ffmpeg", "Gyan.FFmpeg"),
            ["ffprobe"] = ("ffmpeg", "Gyan.FFmpeg"),
            ["magick"] = ("imagemagick", "ImageMagick.ImageMagick"),
            ["qpdf"] = ("qpdf", "QPDF.QPDF")
        };

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

    private static async Task<ToolInstallResult> TryRunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
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
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new(process.ExitCode == 0, commandText, (stdout + stderr).Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, commandText, ex.Message);
        }
    }
}
