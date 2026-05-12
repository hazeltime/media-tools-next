namespace MediaToolsNext.Core;

public sealed record HardwareProfile(
    int ProcessorCount,
    long AvailableMemoryBytes,
    string SourceDriveType,
    string TargetDriveType,
    int RecommendedConcurrency,
    int RecommendedCopyBufferBytes,
    int RecommendedProbeSeconds,
    string Rationale);

