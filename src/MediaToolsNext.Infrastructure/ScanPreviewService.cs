using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScanPreviewService(IFileDiscoverer discoverer) : IScanPreviewService
{
    public async Task<ScanPreview> PreviewAsync(ScanOptions options, CancellationToken cancellationToken, IProgress<ScanPreview>? progress = null)
    {
        var counts = Enum.GetValues<MediaCategory>().ToDictionary(x => x, _ => 0);
        var total = 0;
        // Use a HashSet to count distinct non-empty directory segments so that
        // files in the source root (RelativePath has no directory component,
        // GetDirectoryName returns "" or null) do not inflate the dir count.
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;

        await foreach (var candidate in discoverer.DiscoverAsync(options, cancellationToken))
        {
            total++;
            bytes += candidate.SizeBytes;
            counts[candidate.Category]++;

            var dir = Path.GetDirectoryName(candidate.RelativePath);
            dirs.Add(string.IsNullOrEmpty(dir) ? "." : dir);

            if (total % 250 == 0)
                progress?.Report(new ScanPreview(total, dirs.Count, bytes, counts));
        }

        var preview = new ScanPreview(total, dirs.Count, bytes, counts);
        progress?.Report(preview);
        return preview;
    }
}
