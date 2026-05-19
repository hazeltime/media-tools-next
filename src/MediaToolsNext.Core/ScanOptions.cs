namespace MediaToolsNext.Core;

/// <summary>
/// Represents the configuration options for a media scanning session.
/// </summary>
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
    ValidationDepth ValidationDepth = ValidationDepth.Standard,
    int MaxRetries = 1,
    int? MaxFiles = null,
    int? MaxDirectories = null,
    int ExternalToolTimeoutSeconds = 15,
    int? MaxSearchedFiles = null,
    int? MaxMatchedFiles = null,
    int? MaxSearchedDirectories = null,
    int? MaxMatchedDirectories = null,
    int MinRuntimeBeforeLimitsSeconds = 0,
    long? MinScannedBytes = null,
    long? MaxScannedBytes = null,
    long? MinMatchedBytes = null,
    long? MaxMatchedBytes = null,
    int? MaxRuntimeSeconds = null,
    bool ForceRescan = false,
    ScanLimitState? LimitState = null,
    IReadOnlySet<string>? CustomImageExtensions = null,
    string? CustomImageRegex = null,
    long? MinCandidateBytes = null,
    long? MaxCandidateBytes = null,
    IReadOnlyList<string>? IncludeFileNamePatterns = null,
    IReadOnlyList<string>? ExcludeFileNamePatterns = null,
    IReadOnlySet<ValidationStatus>? ActionStatuses = null,
    FileActionOperation ActionOperation = FileActionOperation.Copy,
    OutputGrouping OutputGrouping = OutputGrouping.Status,
    OutputPathLayout OutputPathLayout = OutputPathLayout.PreserveRelativePath,
    IReadOnlySet<string>? EnabledExtensions = null,
    int CopyBufferBytes = 1024 * 1024)
{
    /// <summary>
    /// Creates a default set of scan options with reasonable defaults.
    /// </summary>
    public static ScanOptions CreateDefault(string sourcePath, string targetRoot) =>
        new(
            sourcePath,
            targetRoot,
            null,
            ScanActionMode.DryRun,
            EnableImages: true,
            EnableVideo: true,
            EnableAudio: true,
            EnableDocuments: true,
            MaxConcurrency: 8,
            MediaProbeSeconds: 120);
}

/// <summary>
/// Tracks reasons for stopping a scan early when limits are reached.
/// </summary>
public sealed class ScanLimitState
{
    /// <summary>
    /// Gets the reason the scan was stopped, if any.
    /// </summary>
    public string? StopReason { get; private set; }

    /// <summary>
    /// Unconditionally sets the stop reason.
    /// </summary>
    public void Stop(string reason)
    {
        StopReason ??= reason;
    }

    /// <summary>
    /// Sets the stop reason, overriding natural exhaustion if work has started.
    /// </summary>
    public void StopAfterWorkStarted(string reason)
    {
        if (StopReason is null or "Source exhausted.")
            StopReason = reason;
    }
}
