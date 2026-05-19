using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class FileActionService : IFileActionService
{
    private readonly Action<string> _deleteFile;

    public FileActionService()
        : this(File.Delete)
    {
    }

    internal FileActionService(Action<string> deleteFile)
    {
        _deleteFile = deleteFile;
    }

    public async Task<FileActionOutcome> ApplyAsync(ValidationOutcome outcome, ScanOptions options, CancellationToken cancellationToken)
    {
        if (options.ActionMode == ScanActionMode.DryRun)
            return FileActionOutcome.DryRun();

        if (options.ActionStatuses is { Count: > 0 } statuses && !statuses.Contains(outcome.Status))
            return new FileActionOutcome("not-written-status-filter", null, null, null);

        var groupFolder = options.OutputGrouping switch
        {
            OutputGrouping.MediaCategory => outcome.Candidate.Category.ToString().ToLowerInvariant(),
            OutputGrouping.None => string.Empty,
            _ => StatusFolder(outcome.Status)
        };
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

        var action = options.ActionOperation == FileActionOperation.Move ? "moved" : "copied";
        if (options.ActionOperation == FileActionOperation.Move)
        {
            try
            {
                _deleteFile(outcome.Candidate.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new MoveDeleteFailedException(
                    "Failed to delete source file after copy during move operation: " + ex.Message,
                    ex,
                    primaryTarget,
                    backupTarget);
            }
        }

        return new FileActionOutcome(
            action,
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

        var sourceInfo = new FileInfo(source);
        var sourceLength = sourceInfo.Length;
        var sourceWriteTime = sourceInfo.LastWriteTimeUtc;

        for (var i = 0; ; i++)
        {
            var candidate = i == 0 ? target : Path.Combine(dir, $"{name}_{i}{ext}");

            if (File.Exists(candidate))
            {
                try
                {
                    var candInfo = new FileInfo(candidate);
                    if (candInfo.Length == sourceLength && candInfo.LastWriteTimeUtc == sourceWriteTime)
                    {
                        return candidate; // Already copied!
                    }
                }
                catch
                {
                    // Ignore errors reading candidate info and proceed
                }
            }

            try
            {
                await CopyAsync(source, candidate, copyBufferBytes, cancellationToken);
                try
                {
                    File.SetLastWriteTimeUtc(candidate, sourceWriteTime);
                }
                catch
                {
                    // Ignore failures to set the write time (e.g. read-only filesystem or locking indexers)
                }
                return candidate;
            }
            catch (IOException ex) when (IsAlreadyExists(ex))
            {
                continue;
            }
        }
    }

    private async Task<(string PrimaryTarget, string BackupTarget)> CopyToSharedAvailablePathsAsync(
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

        var sourceInfo = new FileInfo(source);
        var sourceLength = sourceInfo.Length;
        var sourceWriteTime = sourceInfo.LastWriteTimeUtc;

        for (var i = 0; ; i++)
        {
            var primaryCandidate = i == 0 ? primaryTarget : Path.Combine(primaryDir, $"{primaryName}_{i}{primaryExt}");
            var backupCandidate = i == 0 ? backupTarget : Path.Combine(backupDir, $"{backupName}_{i}{backupExt}");

            // Check if both already exist and match the source
            var primaryOk = false;
            if (File.Exists(primaryCandidate))
            {
                try
                {
                    var pInfo = new FileInfo(primaryCandidate);
                    if (pInfo.Length == sourceLength && pInfo.LastWriteTimeUtc == sourceWriteTime)
                    {
                        primaryOk = true;
                    }
                }
                catch { }
            }

            if (primaryOk && File.Exists(backupCandidate))
            {
                try
                {
                    var bInfo = new FileInfo(backupCandidate);
                    if (bInfo.Length == sourceLength && bInfo.LastWriteTimeUtc == sourceWriteTime)
                    {
                        return (primaryCandidate, backupCandidate); // Already copied to both!
                    }
                }
                catch { }
            }

            // If primary is not yet copied, copy it
            if (!primaryOk)
            {
                try
                {
                    await CopyAsync(source, primaryCandidate, copyBufferBytes, cancellationToken);
                    try
                    {
                        File.SetLastWriteTimeUtc(primaryCandidate, sourceWriteTime);
                    }
                    catch { }
                }
                catch (IOException ex) when (IsAlreadyExists(ex))
                {
                    continue;
                }
            }

            // Copy primary to backup
            try
            {
                await CopyAsync(primaryCandidate, backupCandidate, copyBufferBytes, cancellationToken);
                try
                {
                    File.SetLastWriteTimeUtc(backupCandidate, sourceWriteTime);
                }
                catch { }
                return (primaryCandidate, backupCandidate);
            }
            catch (IOException ex) when (IsAlreadyExists(ex))
            {
                TryDeletePartialPrimary(primaryCandidate);
                continue;
            }
            catch
            {
                TryDeletePartialPrimary(primaryCandidate);
                throw;
            }
        }
    }

    private void TryDeletePartialPrimary(string path)
    {
        try
        {
            _deleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve the original backup write failure; cleanup errors are secondary.
        }
    }

    private static string CombineOutputPath(string root, string statusFolder, string relativePath)
    {
        // Path.Combine discards earlier segments when a later segment is rooted.
        // Validate relativePath so malformed input cannot escape the output root.
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

public sealed class MoveDeleteFailedException : IOException
{
    public string PrimaryTargetPath { get; }
    public string? BackupTargetPath { get; }

    public MoveDeleteFailedException(string message, Exception innerException, string primaryTargetPath, string? backupTargetPath)
        : base(message, innerException)
    {
        PrimaryTargetPath = primaryTargetPath;
        BackupTargetPath = backupTargetPath;
    }
}
