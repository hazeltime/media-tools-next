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
    // Image-only profiles (MVP focus)
    public static readonly ScanProfile FastDryRun = new(
        "quick-images",
        "Quick image check: verifies file exists, is not empty, and has a recognizable image header.",
        ScanActionMode.DryRun,
        EnableImages: true, EnableVideo: false, EnableAudio: false, EnableDocuments: false,
        MediaProbeSeconds: 30,
        ValidationDepth.Fast);

    public static readonly ScanProfile StandardImages = new(
        "standard-images",
        "Standard image check: quick checks plus ImageMagick metadata validation when available.",
        ScanActionMode.DryRun,
        EnableImages: true, EnableVideo: false, EnableAudio: false, EnableDocuments: false,
        MediaProbeSeconds: 60,
        ValidationDepth.Standard);

    public static readonly ScanProfile DeepImages = new(
        "deep-images",
        "Deep image check: asks ImageMagick to read the full image, catching more decode failures at higher cost.",
        ScanActionMode.DryRun,
        EnableImages: true, EnableVideo: false, EnableAudio: false, EnableDocuments: false,
        MediaProbeSeconds: 120,
        ValidationDepth.Deep);

    // All-media profile
    public static readonly ScanProfile AllMedia = new(
        "all-media",
        "Standard scan covering images, video, audio, and documents.",
        ScanActionMode.DryRun,
        EnableImages: true, EnableVideo: true, EnableAudio: true, EnableDocuments: true,
        MediaProbeSeconds: 120,
        ValidationDepth.Standard);

    public static IReadOnlyList<ScanProfile> All { get; } = [FastDryRun, StandardImages, DeepImages, AllMedia];

    /// <summary>
    /// Resolves a profile by name. The shared resolver falls back to
    /// <see cref="FastDryRun"/> when <paramref name="name"/> is null or unknown.
    /// The CLI supplies <see cref="DeepImages"/> when its --profile option is
    /// omitted, so this fallback mainly applies to direct callers and unknown
    /// explicit names. Callers that need all-media scanning must explicitly pass
    /// "all-media".
    /// </summary>
    public static ScanProfile Get(string? name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? FastDryRun;
}
