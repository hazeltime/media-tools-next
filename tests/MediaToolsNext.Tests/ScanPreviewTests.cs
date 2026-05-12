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
}
