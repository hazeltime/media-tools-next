using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class FileActionService : IFileActionService
{
    public async Task<FileActionOutcome> ApplyAsync(ValidationOutcome outcome, ScanOptions options, CancellationToken cancellationToken)
    {
        if (options.ActionMode == ScanActionMode.DryRun)
            return FileActionOutcome.DryRun();

        if (options.ActionStatuses is { Count: > 0 } statuses && !statuses.Contains(outcome.Status))
            return new FileActionOutcome("not-written-status-filter", null, null, null);

        var groupFolder = options.OutputGrouping == OutputGrouping.MediaCategory
            ? outcome.Candidate.Category.ToString().ToLowerInvariant()
            : StatusFolder(outcome.Status);
        var outputPath = options.OutputPathLayout == OutputPathLayout.Flat
            ? Path.GetFileName(outcome.Candidate.RelativePath)
            : outcome.Candidate.RelativePath;

        var primaryTarget = GetSafePath(CombineOutputPath(options.TargetRoot, groupFolder, outputPath));
        string? backupTarget = null;

        // Only write backup when the mode explicitly requests it AND a backup root is configured.
        // CopySorted must never write a backup even if BackupRoot happens to be set.
        if (options.ActionMode == ScanActionMode.CopySortedAndBackup
            && !string.IsNullOrWhiteSpace(options.BackupRoot))
        {
            (primaryTarget, backupTarget) = GetSharedSafePaths(
                CombineOutputPath(options.TargetRoot, groupFolder, outputPath),
                CombineOutputPath(options.BackupRoot, groupFolder, outputPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(primaryTarget)!);
        await CopyAsync(outcome.Candidate.FullPath, primaryTarget, cancellationToken);

        if (backupTarget is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
            await CopyAsync(primaryTarget, backupTarget, cancellationToken);
        }

        if (options.ActionOperation == FileActionOperation.Move)
            File.Delete(outcome.Candidate.FullPath);

        return new FileActionOutcome(
            options.ActionOperation == FileActionOperation.Move ? "moved" : "copied",
            primaryTarget,
            backupTarget,
            null);
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        const int CopyBufferSize = 1024 * 1024;
        await using var input  = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize);
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

    private static string StatusFolder(ValidationStatus status) =>
        status == ValidationStatus.Valid ? "valid" : status.ToString().ToLowerInvariant();

    private static (string PrimaryTarget, string BackupTarget) GetSharedSafePaths(string primaryTarget, string backupTarget)
    {
        if (!File.Exists(primaryTarget) && !File.Exists(backupTarget))
            return (primaryTarget, backupTarget);

        var primaryDir = Path.GetDirectoryName(primaryTarget)!;
        var primaryName = Path.GetFileNameWithoutExtension(primaryTarget);
        var primaryExt = Path.GetExtension(primaryTarget);
        var backupDir = Path.GetDirectoryName(backupTarget)!;
        var backupName = Path.GetFileNameWithoutExtension(backupTarget);
        var backupExt = Path.GetExtension(backupTarget);

        for (var i = 1; ; i++)
        {
            var primarySuffixed = Path.Combine(primaryDir, $"{primaryName}_{i}{primaryExt}");
            var backupSuffixed = Path.Combine(backupDir, $"{backupName}_{i}{backupExt}");
            if (!File.Exists(primarySuffixed) && !File.Exists(backupSuffixed))
                return (primarySuffixed, backupSuffixed);
        }
    }
}
