namespace MediaToolsNext.Core;

public sealed record ValidationOutcome(
    FileCandidate Candidate,
    ValidationStatus Status,
    string Validator,
    string? Detail,
    TimeSpan Duration);

