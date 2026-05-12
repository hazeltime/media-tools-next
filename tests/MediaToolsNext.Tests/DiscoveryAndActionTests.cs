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

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}

