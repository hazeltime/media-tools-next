namespace MediaToolsNext.Core;

public sealed record ScanPreview(int TotalFiles, int TotalDirectories, long TotalBytes, IReadOnlyDictionary<MediaCategory, int> FilesByCategory);
