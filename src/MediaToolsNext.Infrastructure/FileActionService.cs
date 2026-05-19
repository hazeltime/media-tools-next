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

        var primaryBaseTarget = CombineOutputPath(options.TargetRoot, groupFolder, outputPath);
        var copyBufferBytes = Math.Clamp(options.CopyBufferBytes, 16 * 1024, 4 * 1024 * 1024);
        string primaryTarget;
        string? backupTarget = null;

        // Only write backup when the mode explicitly requests it AND a backup root is configured.
        // CopySorted must never write a backup even if BackupRoot happens to be set.
        if (options.ActionMode == ScanActionMode.CopySortedAndBackup
            && !string.IsNullOrWhiteSpace(options.BackupRoot))
        {
            (primaryTarget, backupTarget) = await CopyToSharedAvailablePathsAsync(
                outcome.Candidate.FullPath,
                primaryBaseTarget,
                CombineOutputPath(options.BackupRoot, groupFolder, outputPath),
                copyBufferBytes,
                cancellationToken);
        }
        else
        {
            primaryTarget = await CopyToAvailablePathAsync(outcome.Candidate.FullPath, primaryBaseTarget, copyBufferBytes, cancellationToken);
        }

        if (options.ActionOperation == FileActionOperation.Move)
            File.Delete(outcome.Candidate.FullPath);

        return new FileActionOutcome(
            options.ActionOperation == FileActionOperation.Move ? "moved" : "copied",
            primaryTarget,
            backupTarget,
            null);
    }

    private static async Task CopyAsync(string source, string target, int copyBufferBytes, CancellationToken cancellationToken)
    {
        await using var input  = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, copyBufferBytes);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, copyBufferBytes);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<string> CopyToAvailablePathAsync(string source, string target, int copyBufferBytes, CancellationToken cancellationToken)
    {
        var dir  = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var ext  = Path.GetExtension(target);
        Directory.CreateDirectory(dir);

        for (var i = 0; ; i++)
        {
            var candidate = i == 0 ? target : Path.Combine(dir, $"{name}_{i}{ext}");
            try
            {
                await CopyAsync(source, candidate, copyBufferBytes, cancellationToken);
                return candidate;
            }
            catch (IOException ex) when (IsAlreadyExists(ex))
            {
                continue;
            }
        }
    }

    private static async Task<(string PrimaryTarget, string BackupTarget)> CopyToSharedAvailablePathsAsync(
        string source,
        string primaryTarget,
        string backupTarget,
        int copyBufferBytes,
        CancellationToken cancellationToken)
    {
        var primaryDir = Path.GetDirectoryName(primaryTarget)!;
        var primaryName = Path.GetFileNameWithoutExtension(primaryTarget);
        var primaryExt = Path.GetExtension(primaryTarget);
        var backupDir = Path.GetDirectoryName(backupTarget)!;
        var backupName = Path.GetFileNameWithoutExtension(backupTarget);
        var backupExt = Path.GetExtension(backupTarget);
        Directory.CreateDirectory(primaryDir);
        Directory.CreateDirectory(backupDir);

        for (var i = 0; ; i++)
        {
            var primaryCandidate = i == 0 ? primaryTarget : Path.Combine(primaryDir, $"{primaryName}_{i}{primaryExt}");
            var backupCandidate = i == 0 ? backupTarget : Path.Combine(backupDir, $"{backupName}_{i}{backupExt}");
            try
            {
                await CopyAsync(source, primaryCandidate, copyBufferBytes, cancellationToken);
            }
            catch (IOException ex) when (IsAlreadyExists(ex))
            {
                continue;
            }

            try
            {
                await CopyAsync(primaryCandidate, backupCandidate, copyBufferBytes, cancellationToken);
                return (primaryCandidate, backupCandidate);
            }
            catch (IOException ex) when (IsAlreadyExists(ex))
            {
                File.Delete(primaryCandidate);
                continue;
            }
            catch
            {
                File.Delete(primaryCandidate);
                throw;
            }
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
        if (parts.Any(part => part == "." || part == ".."))
            throw new ArgumentException(
                $"relativePath must not contain traversal segments. Got: {relativePath}",
                nameof(relativePath));

        var combined = Path.GetFullPath(Path.Combine([root, statusFolder, .. parts]));
        var safeRoot = Path.GetFullPath(Path.Combine(root, statusFolder));
        if (!IsSameOrChildPath(safeRoot, combined))
            throw new ArgumentException(
                $"relativePath resolves outside the output root. Got: {relativePath}",
                nameof(relativePath));
        return combined;
    }

    private static string StatusFolder(ValidationStatus status) =>
        status == ValidationStatus.Valid ? "valid" : status.ToString().ToLowerInvariant();

    private static bool IsSameOrChildPath(string parent, string path)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyExists(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 17 or 80 or 183;
    }
}
