namespace MediaToolsNext.Core;

public sealed record FileActionOutcome(
    string Action,
    string? PrimaryTargetPath,
    string? BackupTargetPath,
    string? Detail)
{
    public static FileActionOutcome DryRun() => new("dry-run", null, null, null);
}

