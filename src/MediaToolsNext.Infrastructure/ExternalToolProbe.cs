using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ExternalToolProbe : IExternalToolProbe
{
    private static readonly string[] ToolNames = ["ffmpeg", "ffprobe", "magick", "qpdf"];

    public IReadOnlyList<ToolStatus> GetStatuses() =>
        ToolNames.Select(name =>
        {
            var path = FindExecutable(name);
            return new ToolStatus(name, path is not null, path, path is null ? "Not found on PATH" : null);
        }).ToArray();

    public string? FindExecutable(string commandName)
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
            var found = Directory.Exists(programFiles)
                ? Directory.EnumerateFiles(programFiles, "magick.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (found is not null) return found;
        }

        return null;
    }
}

