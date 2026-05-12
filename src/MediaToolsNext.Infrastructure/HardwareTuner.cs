using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class HardwareTuner : IHardwareTuner
{
    public HardwareProfile Recommend(string sourcePath, string targetPath)
    {
        var cpu = Environment.ProcessorCount;
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var sourceDrive = GetDriveType(sourcePath);
        var targetDrive = GetDriveType(targetPath);
        var sameRoot = string.Equals(Path.GetPathRoot(Path.GetFullPath(sourcePath)), Path.GetPathRoot(Path.GetFullPath(targetPath)), StringComparison.OrdinalIgnoreCase);
        var ioPenalty = sameRoot ? 2 : 1;
        var memoryCap = memory > 0 && memory < 8L * 1024 * 1024 * 1024 ? 2 : 0;
        var concurrency = Math.Clamp((cpu / ioPenalty) - memoryCap, 1, 16);
        var buffer = sourceDrive == "Fixed" && targetDrive == "Fixed" ? 1024 * 1024 : 256 * 1024;
        var probeSeconds = cpu >= 8 ? 120 : 60;
        var rationale = $"{cpu} CPU threads, source={sourceDrive}, target={targetDrive}, sameDrive={sameRoot}";
        return new HardwareProfile(cpu, memory, sourceDrive, targetDrive, concurrency, buffer, probeSeconds, rationale);
    }

    private static string GetDriveType(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return DriveInfo.GetDrives().FirstOrDefault(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase))?.DriveType.ToString() ?? "Unknown";
        }
        catch { return "Unknown"; }
    }
}
