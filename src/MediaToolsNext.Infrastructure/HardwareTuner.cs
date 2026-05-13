using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class HardwareTuner : IHardwareTuner
{
    private const long LowMemoryThresholdBytes = 8L * 1024 * 1024 * 1024;

    public HardwareProfile Recommend(string sourcePath, string targetPath)
    {
        var cpu        = Math.Max(1, Environment.ProcessorCount);
        var memory     = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var sourceDrive = GetDriveType(sourcePath);
        var targetDrive = GetDriveType(targetPath);

        var sameRoot = string.Equals(
            Path.GetPathRoot(Path.GetFullPath(sourcePath)),
            Path.GetPathRoot(Path.GetFullPath(targetPath)),
            StringComparison.OrdinalIgnoreCase);

        var ioPenalty = sameRoot ? 2 : 1;

        var memoryCap = memory > 0 && memory < LowMemoryThresholdBytes ? 2 : 0;

        var concurrency = Math.Clamp((cpu / ioPenalty) - memoryCap, 1, 16);

        // Prefer large I/O buffers when both drives are fixed (SSD/HDD).
        var buffer = sourceDrive == "Fixed" && targetDrive == "Fixed"
            ? 1024 * 1024
            : 256 * 1024;

        // Give slower/single-core machines a shorter probe window so the scan
        // doesn’t stall on a large corrupt video for too long.
        var probeSeconds = cpu >= 8 ? 120 : 60;

        var rationale = $"{cpu} CPU threads, source={sourceDrive}, target={targetDrive}, sameDrive={sameRoot}, memoryCap={memoryCap}, concurrency={concurrency}";
        return new HardwareProfile(cpu, memory, sourceDrive, targetDrive, concurrency, buffer, probeSeconds, rationale);
    }

    private static string GetDriveType(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return DriveInfo.GetDrives()
                .FirstOrDefault(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase))
                ?.DriveType.ToString() ?? "Unknown";
        }
        catch { return "Unknown"; }
    }
}
