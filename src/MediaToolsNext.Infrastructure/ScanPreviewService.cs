using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScanPreviewService(IFileDiscoverer discoverer) : IScanPreviewService
{
    public async Task<ScanPreview> PreviewAsync(ScanOptions options, CancellationToken cancellationToken, IProgress<ScanPreview>? progress = null)
    {
        var counts = Enum.GetValues<MediaCategory>().ToDictionary(x => x, _ => 0);
        var total = 0;
        var dirs = 0;
        long bytes = 0;
        string? lastDir = null;
        await foreach (var candidate in discoverer.DiscoverAsync(options, cancellationToken))
        {
            total++;
            bytes += candidate.SizeBytes;
            counts[candidate.Category]++;
            var dir = Path.GetDirectoryName(candidate.RelativePath);
            if (!string.Equals(dir, lastDir, StringComparison.OrdinalIgnoreCase))
            {
                dirs++;
                lastDir = dir;
            }
            if (total % 250 == 0)
                progress?.Report(new ScanPreview(total, dirs, bytes, counts));
        }
        var preview = new ScanPreview(total, dirs, bytes, counts);
        progress?.Report(preview);
        return preview;
    }
}
