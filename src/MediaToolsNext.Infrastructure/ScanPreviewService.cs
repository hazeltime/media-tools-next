using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScanPreviewService(IFileDiscoverer discoverer) : IScanPreviewService
{
    public async Task<ScanPreview> PreviewAsync(ScanOptions options, CancellationToken cancellationToken, IProgress<ScanPreview>? progress = null)
    {
        var counts = Enum.GetValues<MediaCategory>().ToDictionary(x => x, _ => 0);
        var total = 0;
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        await foreach (var candidate in discoverer.DiscoverAsync(options, cancellationToken))
        {
            total++;
            bytes += candidate.SizeBytes;
            counts[candidate.Category]++;
            dirs.Add(Path.GetDirectoryName(candidate.RelativePath) ?? string.Empty);
            if (total % 250 == 0)
                progress?.Report(new ScanPreview(total, dirs.Count, bytes, counts));
        }
        var preview = new ScanPreview(total, dirs.Count, bytes, counts);
        progress?.Report(preview);
        return preview;
    }
}
