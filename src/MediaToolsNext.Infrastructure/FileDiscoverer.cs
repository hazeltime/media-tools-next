using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class FileDiscoverer : IFileDiscoverer
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "System Volume Information", "$RECYCLE.BIN", "_out", "bin", "obj"
    };

    public async IAsyncEnumerable<FileCandidate> DiscoverAsync(ScanOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(options.SourcePath);
        if (!Directory.Exists(source))
            yield break;

        var pending = new Queue<string>();
        pending.Enqueue(source);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Dequeue();

            foreach (var dir in SafeEnumerateDirectories(current))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(dir)))
                    pending.Enqueue(dir);
            }

            foreach (var file in SafeEnumerateFiles(current))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var category = SupportedMedia.GetCategory(ext);
                if (category == MediaCategory.Unknown || !IsEnabled(category, options))
                    continue;

                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }

                yield return new FileCandidate(
                    info.FullName,
                    Path.GetRelativePath(source, info.FullName),
                    ext,
                    category,
                    info.Exists ? info.Length : 0,
                    info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.MinValue);
                await Task.Yield();
            }
        }
    }

    private static bool IsEnabled(MediaCategory category, ScanOptions options) => category switch
    {
        MediaCategory.Image => options.EnableImages,
        MediaCategory.Video => options.EnableVideo,
        MediaCategory.Audio => options.EnableAudio,
        MediaCategory.Document => options.EnableDocuments,
        _ => false
    };

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path); }
        catch { return []; }
    }
}

