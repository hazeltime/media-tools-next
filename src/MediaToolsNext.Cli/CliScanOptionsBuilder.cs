using MediaToolsNext.Core;

namespace MediaToolsNext.Cli;

public sealed record CliScanOptionsBuildResult(
    ScanOptions Options,
    ScanProfile Profile,
    string? ExportPath,
    HardwareProfile HardwareProfile);

public static class CliScanOptionsBuilder
{
    public static CliScanOptionsBuildResult Build(string[] args, HardwareProfile hardwareProfile, string defaultDatabasePath)
    {
        if (args.Length < 2)
            throw new ArgumentException("At least source and target arguments are required.", nameof(args));

        var source = args[0];
        var target = args[1];
        var backup = ValueAfter(args, "--backup");
        var profile = ScanProfiles.Get(ValueAfter(args, "--profile") ?? ScanProfiles.DeepImages.Name);
        var exportPath = ValueAfter(args, "--export");
        var db = ValueAfter(args, "--db") ?? defaultDatabasePath;
        var concurrency = IntAfter(args, "--concurrency") is int parsedConcurrency
            ? Math.Clamp(parsedConcurrency, 1, 32)
            : hardwareProfile.RecommendedConcurrency;
        var probeSeconds = IntAfter(args, "--probe-seconds") is int parsedProbeSeconds
            ? Math.Clamp(parsedProbeSeconds, 10, 600)
            : hardwareProfile.RecommendedProbeSeconds;
        var mode = args.Contains("--live")
            ? (backup is null ? ScanActionMode.CopySorted : ScanActionMode.CopySortedAndBackup)
            : profile.DefaultActionMode;
        var operation = args.Contains("--move") ? FileActionOperation.Move : FileActionOperation.Copy;
        var grouping = args.Contains("--group-category") ? OutputGrouping.MediaCategory : OutputGrouping.Status;
        var layout = args.Contains("--flat") ? OutputPathLayout.Flat : OutputPathLayout.PreserveRelativePath;

        var options = new ScanOptions(
            source,
            target,
            backup,
            mode,
            profile.EnableImages,
            profile.EnableVideo,
            profile.EnableAudio,
            profile.EnableDocuments,
            concurrency,
            probeSeconds,
            db,
            profile.ValidationDepth,
            MaxSearchedFiles: IntAfter(args, "--max-searched-files"),
            MaxMatchedFiles: IntAfter(args, "--max-matched-files"),
            MaxSearchedDirectories: IntAfter(args, "--max-searched-dirs"),
            MaxMatchedDirectories: IntAfter(args, "--max-matched-dirs"),
            MinRuntimeBeforeLimitsSeconds: IntAfter(args, "--min-runtime-seconds") ?? 0,
            MinScannedBytes: MbAfter(args, "--min-scanned-mb"),
            MaxScannedBytes: MbAfter(args, "--max-scanned-mb"),
            MinMatchedBytes: MbAfter(args, "--min-matched-mb"),
            MaxMatchedBytes: MbAfter(args, "--max-matched-mb"),
            ExternalToolTimeoutSeconds: IntAfter(args, "--tool-timeout-seconds") ?? 15,
            ActionOperation: operation,
            OutputGrouping: grouping,
            OutputPathLayout: layout,
            CopyBufferBytes: hardwareProfile.RecommendedCopyBufferBytes);

        return new CliScanOptionsBuildResult(options, profile, exportPath, hardwareProfile);
    }

    private static string? ValueAfter(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int? IntAfter(string[] args, string name) =>
        int.TryParse(ValueAfter(args, name), out var value) && value > 0 ? value : null;

    private static long? MbAfter(string[] args, string name) =>
        IntAfter(args, name) is int value ? value * 1048576L : null;
}
