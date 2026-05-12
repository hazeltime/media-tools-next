namespace MediaToolsNext.Core;

public sealed record ScanPreview(int TotalFiles, long TotalBytes, IReadOnlyDictionary<MediaCategory, int> FilesByCategory);

