using MediaToolsNext.Core;

namespace MediaToolsNext.Tests;

public class ScanPerformanceTests
{
    [Fact]
    public void TrackerAccumulatesFilesAndBytes()
    {
        var tracker = new ScanPerformanceTracker();
        var candidate = new FileCandidate("a", "a", ".txt", MediaCategory.Document, 1024, DateTimeOffset.UtcNow);
        var perf = tracker.Add(new ScanResultRecord(Guid.NewGuid(), candidate, ValidationStatus.Valid, "test", null, "dry-run", null, null, DateTimeOffset.UtcNow));
        Assert.Equal(1, perf.FilesProcessed);
        Assert.Equal(1024, perf.BytesProcessed);
    }
}
