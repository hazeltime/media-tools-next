namespace MediaToolsNext.Core;

public sealed record ScanPerformance(TimeSpan Elapsed, int FilesProcessed, long BytesProcessed)
{
    public double FilesPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : FilesProcessed / Elapsed.TotalSeconds;
    public double MegabytesPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : BytesProcessed / 1048576d / Elapsed.TotalSeconds;
}

public sealed class ScanPerformanceTracker
{
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private int _files;
    private long _bytes;

    public ScanPerformance Add(ScanResultRecord record)
    {
        Interlocked.Increment(ref _files);
        Interlocked.Add(ref _bytes, record.Candidate.SizeBytes);
        return Current;
    }

    public ScanPerformance Current => new(DateTimeOffset.UtcNow - _started, _files, _bytes);
}
