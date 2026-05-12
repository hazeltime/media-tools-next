using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class FileActionService : IFileActionService
{
    public async Task<FileActionOutcome> ApplyAsync(ValidationOutcome outcome, ScanOptions options, CancellationToken cancellationToken)
    {
        if (options.ActionMode == ScanActionMode.DryRun)
            return FileActionOutcome.DryRun();

        if (options.ActionStatuses is { Count: > 0 } statuses && !statuses.Contains(outcome.Status))
            return new FileActionOutcome("not-copied-status-filter", null, null, null);

        var statusFolder = outcome.Status == ValidationStatus.Valid
            ? "valid"
            : outcome.Status.ToString().ToLowerInvariant();

        var primaryTarget = GetSafePath(CombineOutputPath(options.TargetRoot, statusFolder, outcome.Candidate.RelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(primaryTarget)!);
        await CopyAsync(outcome.Candidate.FullPath, primaryTarget, cancellationToken);

        string? backupTarget = null;
        // Only write backup when the mode explicitly requests it AND a backup root is configured.
        // CopySorted must never write a backup even if BackupRoot happens to be set.
        if (options.ActionMode == ScanActionMode.CopySortedAndBackup
            && !string.IsNullOrWhiteSpace(options.BackupRoot))
        {
            backupTarget = GetSafePath(CombineOutputPath(options.BackupRoot, statusFolder, outcome.Candidate.RelativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
            await CopyAsync(primaryTarget, backupTarget, cancellationToken);
        }

        return new FileActionOutcome("copied", primaryTarget, backupTarget, null);
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input  = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = File.Create(target);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string GetSafePath(string target)
    {
        if (!File.Exists(target)) return target;
        var dir  = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var ext  = Path.GetExtension(target);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string CombineOutputPath(string root, string statusFolder, string relativePath)
    {
        // BUG FIX: Path.Combine discards earlier segments when a later segment is
        // rooted (absolute). Sanitize relativePath to prevent a rooted relative
        // path from hijacking the output location — e.g. if source normalisation
        // produces an absolute path as the relative segment.
        var safeRelative = relativePath
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.IsPathRooted(safeRelative))
            throw new ArgumentException(
                $"relativePath must not be an absolute path. Got: {relativePath}",
                nameof(relativePath));

        var parts = safeRelative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine([root, statusFolder, .. parts]);
    }
}
