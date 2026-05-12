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

        var statusFolder = outcome.Status == ValidationStatus.Valid ? "valid" : outcome.Status.ToString().ToLowerInvariant();
        var primaryTarget = GetSafePath(CombineOutputPath(options.TargetRoot, statusFolder, outcome.Candidate.RelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(primaryTarget)!);
        await CopyAsync(outcome.Candidate.FullPath, primaryTarget, cancellationToken);

        string? backupTarget = null;
        if (options.ActionMode == ScanActionMode.CopySortedAndBackup && !string.IsNullOrWhiteSpace(options.BackupRoot))
        {
            backupTarget = GetSafePath(CombineOutputPath(options.BackupRoot, statusFolder, outcome.Candidate.RelativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
            await CopyAsync(primaryTarget, backupTarget, cancellationToken);
        }

        return new FileActionOutcome("copied", primaryTarget, backupTarget, null);
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = File.Create(target);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string GetSafePath(string target)
    {
        if (!File.Exists(target)) return target;
        var dir = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string CombineOutputPath(string root, string statusFolder, string relativePath)
    {
        var parts = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine([root, statusFolder, .. parts]);
    }
}
