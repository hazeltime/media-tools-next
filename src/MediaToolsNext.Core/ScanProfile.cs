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
    public static readonly ScanProfile FastDryRun = new("quick-images", "Quick image check: verifies file exists, is not empty, and has a recognizable image header.", ScanActionMode.DryRun, true, false, false, false, 30, ValidationDepth.Fast);
    public static readonly ScanProfile StandardImages = new("standard-images", "Standard image check: quick checks plus ImageMagick metadata validation when available.", ScanActionMode.DryRun, true, false, false, false, 60, ValidationDepth.Standard);
    public static readonly ScanProfile DeepImages = new("deep-images", "Deep image check: asks ImageMagick to read the full image, catching more decode failures at higher cost.", ScanActionMode.DryRun, true, false, false, false, 120, ValidationDepth.Deep);

    public static IReadOnlyList<ScanProfile> All { get; } = [FastDryRun, StandardImages, DeepImages];

    public static ScanProfile Get(string? name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? FastDryRun;
}
