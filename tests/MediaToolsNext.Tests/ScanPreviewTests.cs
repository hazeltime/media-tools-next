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
            Assert.Equal(1, preview.TotalDirectories);
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
    public async Task PreviewReportsFirstReachedFileLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "b.txt"), "hello");
            var limitState = new ScanLimitState();
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with
            {
                MaxMatchedFiles = 1,
                LimitState = limitState
            };

            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);

            Assert.Equal(1, preview.TotalFiles);
            Assert.Equal("Stopped after matching 1 files.", limitState.StopReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewDelaysFileLimitUntilMinimumRuntimeElapsed()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "b.txt"), "hello");
            var limitState = new ScanLimitState();
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with
            {
                MaxMatchedFiles = 1,
                MinRuntimeBeforeLimitsSeconds = 1,
                LimitState = limitState
            };

            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);

            Assert.Equal(2, preview.TotalFiles);
            Assert.Equal("Source exhausted.", limitState.StopReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewReportsFirstReachedMatchedMbLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "b.txt"), "hello");
            var limitState = new ScanLimitState();
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with
            {
                MaxMatchedBytes = 1,
                LimitState = limitState
            };

            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);

            Assert.Equal(0, preview.TotalFiles);
            Assert.Equal("Stopped before exceeding the total matched size limit of 1 B.", limitState.StopReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewAppliesMaxMatchedBytesOnceMinimumMatchedBytesIsReached()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "12345");
            File.WriteAllText(Path.Combine(root, "b.txt"), "12345");
            var limitState = new ScanLimitState();
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with
            {
                MinMatchedBytes = 5,
                MaxMatchedBytes = 6,
                LimitState = limitState
            };

            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);

            Assert.Equal(1, preview.TotalFiles);
            Assert.Equal(5, preview.TotalBytes);
            Assert.Equal("Stopped before exceeding the total matched size limit of 6 B.", limitState.StopReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewStopsBeforeTotalMatchedSizeWouldExceedLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "12345");
            File.WriteAllText(Path.Combine(root, "b.txt"), "12345");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with
            {
                MaxMatchedBytes = 6
            };

            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);

            Assert.Equal(1, preview.TotalFiles);
            Assert.Equal(5, preview.TotalBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreviewReportsFirstReachedTimeLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var limitState = new ScanLimitState();
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with
            {
                MaxRuntimeSeconds = 0,
                LimitState = limitState
            };

            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);

            Assert.Equal(1, preview.TotalFiles);
            Assert.Equal("Source exhausted.", limitState.StopReason);
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

    [Fact]
    public async Task PreviewMaxMatchedDirectoriesIncludesAllFilesInAllowedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "one"));
        Directory.CreateDirectory(Path.Combine(root, "two"));
        try
        {
            File.WriteAllText(Path.Combine(root, "one", "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "one", "b.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "two", "c.txt"), "hello");
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "out"), Path.Combine(root, "db.sqlite")) with { MaxMatchedDirectories = 1 };
            var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);
            Assert.Equal(2, preview.TotalFiles);
            Assert.Equal(1, preview.TotalDirectories);
        }
        finally { Directory.Delete(root, true); }
    }
}
