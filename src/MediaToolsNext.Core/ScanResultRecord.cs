namespace MediaToolsNext.Core;

public sealed record ScanResultRecord(
    Guid SessionId,
    FileCandidate Candidate,
    ValidationStatus Status,
    string Validator,
    string? Detail,
    string Action,
    string? PrimaryTargetPath,
    string? BackupTargetPath,
    DateTimeOffset TimestampUtc);

