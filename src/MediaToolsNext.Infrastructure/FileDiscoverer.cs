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
        var started = DateTimeOffset.UtcNow;
        var searchedDirs = 0;
        var searchedFiles = 0;
        var matchedFiles = 0;
        var scannedBytes = 0L;
        var matchedBytes = 0L;
        var matchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxSearchedDirs = options.MaxSearchedDirectories ?? options.MaxDirectories;
        var maxMatchedFiles = options.MaxMatchedFiles ?? options.MaxFiles;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LimitsActive() && maxSearchedDirs is int maxDirs && searchedDirs >= maxDirs)
                yield break;

            var current = pending.Dequeue();
            searchedDirs++;

            foreach (var dir in SafeEnumerateDirectories(current))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(dir)))
                    pending.Enqueue(dir);
            }

            foreach (var file in SafeEnumerateFiles(current))
            {
                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }

                searchedFiles++;
                scannedBytes += info.Exists ? info.Length : 0;
                if (LimitsActive() && ShouldStopBeforeNextMatch())
                    yield break;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                var category = SupportedMedia.GetCategory(ext);
                if (category == MediaCategory.Unknown || !IsEnabled(category, options))
                    continue;

                matchedFiles++;
                matchedBytes += info.Exists ? info.Length : 0;
                var matchDir = Path.GetDirectoryName(info.FullName);
                if (!string.IsNullOrWhiteSpace(matchDir))
                    matchedDirs.Add(matchDir);

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

        bool LimitsActive()
        {
            var runtimeOk = (DateTimeOffset.UtcNow - started).TotalSeconds >= Math.Max(0, options.MinRuntimeBeforeLimitsSeconds);
            var minScannedOk = options.MinScannedBytes is not long minScanned || scannedBytes >= minScanned;
            var minMatchedOk = options.MinMatchedBytes is not long minMatched || matchedBytes >= minMatched;
            return runtimeOk && minScannedOk && minMatchedOk;
        }

        bool ShouldStopBeforeNextMatch()
        {
            if (options.MaxSearchedFiles is int maxSearchedFiles && searchedFiles > maxSearchedFiles) return true;
            if (options.MaxScannedBytes is long maxScannedBytes && scannedBytes > maxScannedBytes) return true;
            if (maxMatchedFiles is int maxFiles && matchedFiles >= maxFiles) return true;
            if (options.MaxMatchedDirectories is int maxMatchedDirs && matchedDirs.Count >= maxMatchedDirs) return true;
            if (options.MaxMatchedBytes is long maxMatchedBytes && matchedBytes >= maxMatchedBytes) return true;
            return false;
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
