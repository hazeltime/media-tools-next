namespace MediaToolsNext.Core;

public sealed record ScanProfile(
    string Name,
    string Description,
    ScanActionMode DefaultActionMode,
    bool EnableImages,
    bool EnableVideo,
    bool EnableAudio,
    bool EnableDocuments,
    int MediaProbeSeconds,
    ValidationDepth ValidationDepth);

public static class ScanProfiles
{
    public static readonly ScanProfile FastDryRun = new("fast", "Fast dry-run scan across common media.", ScanActionMode.DryRun, true, true, true, true, 60, ValidationDepth.Fast);
    public static readonly ScanProfile DeepSort = new("deep-sort", "Deeper validation with sorted copy output.", ScanActionMode.CopySorted, true, true, true, true, 180, ValidationDepth.Deep);
    public static readonly ScanProfile PhotosOnly = new("photos", "Image-focused corruption scan.", ScanActionMode.DryRun, true, false, false, false, 120, ValidationDepth.Standard);
    public static readonly ScanProfile VideoAudit = new("video", "Video/audio stream audit.", ScanActionMode.DryRun, false, true, true, false, 120, ValidationDepth.Standard);

    public static IReadOnlyList<ScanProfile> All { get; } = [FastDryRun, DeepSort, PhotosOnly, VideoAudit];

    public static ScanProfile Get(string? name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? FastDryRun;
}
