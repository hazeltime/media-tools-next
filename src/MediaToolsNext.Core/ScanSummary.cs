namespace MediaToolsNext.Core;

public sealed record ScanSummary(
    Guid SessionId,
    int Total,
    int Valid,
    int Corrupt,
    int Unknown,
    int Errors,
    int Skipped);

