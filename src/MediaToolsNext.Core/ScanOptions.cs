namespace MediaToolsNext.Core;

public sealed record ScanOptions(
    string SourcePath,
    string TargetRoot,
    string? BackupRoot,
    ScanActionMode ActionMode,
    bool EnableImages,
    bool EnableVideo,
    bool EnableAudio,
    bool EnableDocuments,
    int MaxConcurrency,
    int MediaProbeSeconds,
    string DatabasePath,
    ValidationDepth ValidationDepth = ValidationDepth.Standard)
{
    public static ScanOptions CreateDefault(string sourcePath, string targetRoot, string databasePath) =>
        new(
            sourcePath,
            targetRoot,
            null,
            ScanActionMode.DryRun,
            EnableImages: true,
            EnableVideo: true,
            EnableAudio: true,
            EnableDocuments: true,
            MaxConcurrency: Math.Max(1, Environment.ProcessorCount / 2),
            MediaProbeSeconds: 120,
            databasePath);
}
