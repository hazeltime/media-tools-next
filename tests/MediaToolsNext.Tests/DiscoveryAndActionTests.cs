using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class DiscoveryAndActionTests
{
    [Fact]
    public async Task DiscoverySkipsOutputAndFindsSupportedFiles()
    {
        var root = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_out"));
            File.WriteAllText(Path.Combine(root, "photo.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "_out", "ignored.jpg"), "x");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite"));
            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);
            Assert.Single(files);
            Assert.Equal("photo.jpg", files[0].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverySkipsConfiguredTargetAndBackupInsideSource()
    {
        var root = NewTempDir();
        try
        {
            var target = Path.Combine(root, "sorted-output");
            var backup = Path.Combine(root, "backup-output");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(root, "photo.jpg"), "x");
            File.WriteAllText(Path.Combine(target, "target-copy.jpg"), "x");
            File.WriteAllText(Path.Combine(backup, "backup-copy.jpg"), "x");

            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                BackupRoot = backup
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal("photo.jpg", files[0].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryIncludesCustomImageExtensions()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "image.jxl"), "x");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                CustomImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jxl" }
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal(MediaCategory.Image, files[0].Category);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryIncludesCustomImageRegexMatches()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "IMG_001.bin"), "x");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                CustomImageRegex = "^IMG_.*\\.bin$"
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal(MediaCategory.Image, files[0].Category);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverySkipsDeselectedDefaultExtension()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "photo.jpg"), "x");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                EnabledExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" }
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Empty(files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryIncludesSelectedDefaultExtension()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "photo.jpg"), "x");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                EnabledExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg" }
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal(MediaCategory.Image, files[0].Category);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryAppliesSizeAndWildcardFilters()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "IMG_keep.jpg"), new string('x', 2048));
            File.WriteAllText(Path.Combine(root, "IMG_small.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "thumb_keep.jpg"), new string('x', 2048));
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                MinCandidateBytes = 1024,
                IncludeFileNamePatterns = ["IMG_*"],
                ExcludeFileNamePatterns = ["*_small.*"]
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal("IMG_keep.jpg", files[0].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryAppliesFiltersBeforeMatchedFileLimit()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "A_too_small.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "B_keep.jpg"), new string('x', 2048));
            File.WriteAllText(Path.Combine(root, "C_second.jpg"), new string('x', 2048));
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                MinCandidateBytes = 1024,
                MaxMatchedFiles = 1
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal("B_keep.jpg", files[0].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryAppliesMaxCandidateSizeBeforeMatchedFileLimit()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "A_too_large.jpg"), new string('x', 2048));
            File.WriteAllText(Path.Combine(root, "B_keep.jpg"), "ok");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                MaxCandidateBytes = 1024,
                MaxMatchedFiles = 1
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal("B_keep.jpg", files[0].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryDelaysMaxMatchedBytesUntilMinimumIsReached()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "A_keep.jpg"), "12345");
            File.WriteAllText(Path.Combine(root, "B_keep.jpg"), "12345");
            File.WriteAllText(Path.Combine(root, "C_stop.jpg"), "12345");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                MinMatchedBytes = 10,
                MaxMatchedBytes = 6
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Equal(2, files.Count);
            Assert.Equal(["A_keep.jpg", "B_keep.jpg"], files.Select(x => x.RelativePath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryDelaysMaxScannedBytesUntilMinimumIsReached()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "A_keep.jpg"), "12345");
            File.WriteAllText(Path.Combine(root, "B_keep.jpg"), "12345");
            File.WriteAllText(Path.Combine(root, "C_stop.jpg"), "12345");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                MinScannedBytes = 11,
                MaxScannedBytes = 6
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Equal(2, files.Count);
            Assert.Equal(["A_keep.jpg", "B_keep.jpg"], files.Select(x => x.RelativePath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryDoesNotSearchPastMaxSearchedFilesLimit()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "A_keep.jpg"), "12345");
            File.WriteAllText(Path.Combine(root, "B_stop.jpg"), "12345");
            var searched = 0;
            var progress = new SyncProgress<ScanDiscoveryEvent>(e =>
            {
                if (e.Type == DiscoveryEventType.Searched)
                    searched++;
            });
            var limitState = new ScanLimitState();
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                MaxSearchedFiles = 1,
                LimitState = limitState
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None, progress))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal(1, searched);
            Assert.Equal("Stopped after inspecting 1 searched files.", limitState.StopReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryAppliesWildcardFiltersBeforeMatchedFileLimit()
    {
        var root = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "A_skip.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "B_keep.jpg"), "x");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite")) with
            {
                EnableVideo = false,
                EnableAudio = false,
                EnableDocuments = false,
                IncludeFileNamePatterns = ["B_*"],
                MaxMatchedFiles = 1
            };

            var files = new List<FileCandidate>();
            await foreach (var file in new FileDiscoverer().DiscoverAsync(options, CancellationToken.None))
                files.Add(file);

            Assert.Single(files);
            Assert.Equal("B_keep.jpg", files[0].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DryRunActionDoesNotCopy()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), Path.Combine(root, "db.sqlite"));
            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);
            Assert.Equal("dry-run", action.Action);
            Assert.False(Directory.Exists(Path.Combine(root, "target")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedWritesStatusFolderCopy()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "docs/a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted
            };

            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);

            var copied = Path.Combine(target, "valid", "docs", "a.txt");
            Assert.Equal("copied", action.Action);
            Assert.Equal(copied, action.PrimaryTargetPath);
            Assert.True(File.Exists(copied));
            Assert.Equal("hello", File.ReadAllText(copied));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedAndBackupWritesTargetAndBackupCopies()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            var backup = Path.Combine(root, "backup");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "docs/a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Corrupt, "test", "broken", TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySortedAndBackup,
                BackupRoot = backup
            };

            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);

            var targetCopy = Path.Combine(target, "corrupt", "docs", "a.txt");
            var backupCopy = Path.Combine(backup, "corrupt", "docs", "a.txt");
            Assert.Equal("copied", action.Action);
            Assert.Equal(targetCopy, action.PrimaryTargetPath);
            Assert.Equal(backupCopy, action.BackupTargetPath);
            Assert.True(File.Exists(targetCopy));
            Assert.True(File.Exists(backupCopy));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedAndBackupUsesSharedSuffixWhenTargetAlreadyExists()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            var backup = Path.Combine(root, "backup");
            File.WriteAllText(source, "hello");
            Directory.CreateDirectory(Path.Combine(target, "valid", "docs"));
            File.WriteAllText(Path.Combine(target, "valid", "docs", "a.txt"), "existing");
            var candidate = new FileCandidate(source, "docs/a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySortedAndBackup,
                BackupRoot = backup
            };

            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);

            var targetCopy = Path.Combine(target, "valid", "docs", "a_1.txt");
            var backupCopy = Path.Combine(backup, "valid", "docs", "a_1.txt");
            Assert.Equal(targetCopy, action.PrimaryTargetPath);
            Assert.Equal(backupCopy, action.BackupTargetPath);
            Assert.Equal("hello", File.ReadAllText(targetCopy));
            Assert.Equal("hello", File.ReadAllText(backupCopy));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedAndBackupPreservesBackupWriteFailureWhenCleanupFails()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            var backup = Path.Combine(root, "backup");
            File.WriteAllText(source, "hello");
            Directory.CreateDirectory(Path.Combine(backup, "valid", "docs", "a.txt"));
            var candidate = new FileCandidate(source, "docs/a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySortedAndBackup,
                BackupRoot = backup
            };
            var service = new FileActionService(_ => throw new IOException("cleanup failed"));

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => service.ApplyAsync(outcome, options, CancellationToken.None));

            Assert.DoesNotContain("cleanup failed", ex.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedSkipsUnselectedOutcomes()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted,
                ActionStatuses = new HashSet<ValidationStatus> { ValidationStatus.Corrupt }
            };

            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);

            Assert.Equal("not-written-status-filter", action.Action);
            Assert.False(Directory.Exists(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedCanWriteFlatCategoryOutput()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "docs/a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted,
                OutputGrouping = OutputGrouping.MediaCategory,
                OutputPathLayout = OutputPathLayout.Flat
            };

            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);

            var copied = Path.Combine(target, "document", "a.txt");
            Assert.Equal("copied", action.Action);
            Assert.Equal(copied, action.PrimaryTargetPath);
            Assert.True(File.Exists(copied));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedWritesDuplicateFlatNamesToDistinctTargetsInParallel()
    {
        var root = NewTempDir();
        try
        {
            var target = Path.Combine(root, "target");
            var service = new FileActionService();
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted,
                OutputPathLayout = OutputPathLayout.Flat
            };

            var outcomes = Enumerable.Range(0, 24)
                .Select(i =>
                {
                    var sourceDir = Path.Combine(root, "source-" + i);
                    Directory.CreateDirectory(sourceDir);
                    var source = Path.Combine(sourceDir, "same.txt");
                    File.WriteAllText(source, "content-" + i);
                    var candidate = new FileCandidate(source, Path.Combine("source-" + i, "same.txt"), ".txt", MediaCategory.Document, 9, DateTimeOffset.UtcNow);
                    return new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
                })
                .ToArray();

            var actions = await Task.WhenAll(outcomes.Select(outcome => service.ApplyAsync(outcome, options, CancellationToken.None)));

            var targets = actions.Select(action => action.PrimaryTargetPath).ToArray();
            Assert.Equal(outcomes.Length, targets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(targets, path => Assert.True(File.Exists(path)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CopySortedRejectsTraversalInRelativePath()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, Path.Combine("..", "escape.txt"), ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted
            };

            await Assert.ThrowsAsync<ArgumentException>(() => new FileActionService().ApplyAsync(outcome, options, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MoveSortedDeletesSourceAfterTargetWrite()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted,
                ActionOperation = FileActionOperation.Move
            };

            var action = await new FileActionService().ApplyAsync(outcome, options, CancellationToken.None);

            Assert.Equal("moved", action.Action);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(Path.Combine(target, "valid", "a.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MoveSortedReportsDeleteFailureWithoutDuplicatingTarget()
    {
        var root = NewTempDir();
        try
        {
            var source = Path.Combine(root, "a.txt");
            var target = Path.Combine(root, "target");
            File.WriteAllText(source, "hello");
            var candidate = new FileCandidate(source, "a.txt", ".txt", MediaCategory.Document, 5, DateTimeOffset.UtcNow);
            var outcome = new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            var options = ScanOptions.CreateDefault(root, target, Path.Combine(root, "db.sqlite")) with
            {
                ActionMode = ScanActionMode.CopySorted,
                ActionOperation = FileActionOperation.Move
            };
            var service = new FileActionService(_ => throw new IOException("source is locked"));

            var action = await service.ApplyAsync(outcome, options, CancellationToken.None);

            Assert.StartsWith("move-delete-failed: IOException:", action.Action);
            Assert.Equal(Path.Combine(target, "valid", "a.txt"), action.PrimaryTargetPath);
            Assert.True(File.Exists(source));
            Assert.Equal(["a.txt"], Directory.GetFiles(Path.Combine(target, "valid")).Select(path => Path.GetFileName(path)!).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
