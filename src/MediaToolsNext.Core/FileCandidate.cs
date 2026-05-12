namespace MediaToolsNext.Core;

public sealed record FileCandidate(
    string FullPath,
    string RelativePath,
    string Extension,
    MediaCategory Category,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);

