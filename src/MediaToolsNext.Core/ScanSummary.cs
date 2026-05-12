namespace MediaToolsNext.Core;

public sealed record ScanSummary(
    Guid SessionId,
    int Total,
    int Valid,
    int Corrupt,
    int Unknown,
    int Errors,
    int Skipped,
    string CompletionReason = "Source exhausted");

public sealed record ScanSessionRecord(
    Guid SessionId,
    string SourcePath,
    string TargetRoot,
    string? BackupRoot,
    ScanActionMode ActionMode,
    DateTimeOffset StartedUtc);
