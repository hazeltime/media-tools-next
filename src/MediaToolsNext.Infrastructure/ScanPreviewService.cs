using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScanPreviewService(IFileDiscoverer discoverer) : IScanPreviewService
{
    public async Task<ScanPreview> PreviewAsync(ScanOptions options, CancellationToken cancellationToken)
    {
        var counts = Enum.GetValues<MediaCategory>().ToDictionary(x => x, _ => 0);
        var total = 0;
        long bytes = 0;
        await foreach (var candidate in discoverer.DiscoverAsync(options, cancellationToken))
        {
            total++;
            bytes += candidate.SizeBytes;
            counts[candidate.Category]++;
        }
        return new ScanPreview(total, bytes, counts);
    }
}
