using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ScanPreviewTests
{
    [Fact]
    public async Task PreviewCountsDiscoveredFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite"));
            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);
            Assert.Equal(1, preview.TotalFiles);
            Assert.Equal(1, preview.FilesByCategory[MediaCategory.Document]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewRespectsMaxFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "b.txt"), "hello");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with { MaxMatchedFiles = 1 };
            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);
            Assert.Equal(1, preview.TotalFiles);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewRespectsMaxSearchedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "b.txt"), "hello");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with { MaxSearchedFiles = 1 };
            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);
            Assert.Equal(1, preview.TotalFiles);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewRespectsMaxMatchedDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "one"));
        Directory.CreateDirectory(Path.Combine(root, "two"));
        try
        {
            File.WriteAllText(Path.Combine(root, "one", "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "two", "b.txt"), "hello");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with { MaxMatchedDirectories = 1 };
            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);
            Assert.Equal(1, preview.TotalFiles);
        }
        finally { Directory.Delete(root, true); }
    }
}
