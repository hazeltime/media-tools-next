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
                return new ToolStatus(name, path is not null, path, path is null ? "Not found on PATH" : null);
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
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(programFiles))
            {
                var found = Directory.EnumerateFiles(programFiles, "magick.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (found is not null) return found;
            }
        }

        return null;
    }
}
