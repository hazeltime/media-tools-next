namespace MediaToolsNext.Core;

/// <summary>
/// Emitted by IFileDiscoverer for every file it touches, regardless of whether
/// the file passes filters and enters the validation pipeline.
/// Consumers use these to drive live counters (Searched, FilteredOut*).
/// </summary>
public enum DiscoveryEventType
{
    /// <summary>File was touched by the discoverer (counted before any filter).</summary>
    Searched,
    /// <summary>Dropped because file size was outside min/max bounds.</summary>
    FilteredOutSize,
    /// <summary>Dropped because filename did not match include/exclude patterns.</summary>
    FilteredOutPattern,
    /// <summary>Dropped because the file's media family is not enabled.</summary>
    FilteredOutFamily,
}

public readonly record struct ScanDiscoveryEvent(DiscoveryEventType Type);
