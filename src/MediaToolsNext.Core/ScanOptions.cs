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
    ValidationDepth ValidationDepth = ValidationDepth.Standard,
    int MaxRetries = 1,
    int? MaxFiles = null,
    int? MaxDirectories = null,
    int ExternalToolTimeoutSeconds = 20,
    int? MaxSearchedFiles = null,
    int? MaxMatchedFiles = null,
    int? MaxSearchedDirectories = null,
    int? MaxMatchedDirectories = null,
    int MinRuntimeBeforeLimitsSeconds = 0,
    long? MinScannedBytes = null,
    long? MaxScannedBytes = null,
    long? MinMatchedBytes = null,
    long? MaxMatchedBytes = null)
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
