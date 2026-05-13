using System.Collections.Concurrent;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ExternalToolProbe : IExternalToolProbe
{
    private static readonly string[] ToolNames = ["ffmpeg", "ffprobe", "magick", "qpdf"];

    // ExecutionAndPublication ensures the Lazy is initialised once even if multiple
    // threads race to read _statuses.Value for the first time.
    private readonly Lazy<IReadOnlyList<ToolStatus>> _statuses;

    // ConcurrentDictionary eliminates the race where two threads both call
    // ResolveExecutable for the same key before either has cached the result.
    private readonly ConcurrentDictionary<string, string?> _pathCache =
        new(StringComparer.OrdinalIgnoreCase);

    public ExternalToolProbe()
    {
        _statuses = new Lazy<IReadOnlyList<ToolStatus>>(
            () => ToolNames.Select(name =>
            {
                var path = FindExecutable(name);
                return new ToolStatus(name, path is not null, path, path is null ? "Not found on PATH" : null, path is null ? null : ReadVersion(name, path));
            }).ToArray(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<ToolStatus> GetStatuses() => _statuses.Value;

    public string? FindExecutable(string commandName) =>
        _pathCache.GetOrAdd(commandName, ResolveExecutable);

    private static string? ResolveExecutable(string commandName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? commandName + ".exe" : commandName);
            if (File.Exists(candidate)) return candidate;
        }

        if (OperatingSystem.IsWindows() && commandName == "magick")
        {
            foreach (var candidate in GetMagickFallbackCandidates())
            {
                if (File.Exists(candidate)) return candidate;

                try
                {
                    var parent = Path.GetDirectoryName(candidate);
                    if (parent is not null && Directory.Exists(parent))
                    {
                        var found = Directory.EnumerateFiles(parent, Path.GetFileName(candidate), SearchOption.TopDirectoryOnly)
                            .FirstOrDefault();
                        if (found is not null) return found;
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetMagickFallbackCandidates()
    {
        foreach (var root in GetProgramFilesRoots())
        {
            if (!Directory.Exists(root)) continue;

            yield return Path.Combine(root, "magick.exe");

            foreach (var installDir in Directory.EnumerateDirectories(root, "ImageMagick*", SearchOption.TopDirectoryOnly))
            {
                yield return Path.Combine(installDir, "magick.exe");
            }
        }
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return programFiles;

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86) &&
            !string.Equals(programFilesX86, programFiles, StringComparison.OrdinalIgnoreCase))
        {
            yield return programFilesX86;
        }
    }

    private static string? ReadVersion(string name, string path)
    {
        var arguments = name switch
        {
            "magick" => "-version",
            "qpdf" => "--version",
            _ => "-version"
        };

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var output = (process.StandardOutput.ReadLine() ?? process.StandardError.ReadLine())?.Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
}
