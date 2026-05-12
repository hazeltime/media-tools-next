using MediaToolsNext.Core;
using System.Text.RegularExpressions;

namespace MediaToolsNext.Infrastructure;

public sealed class FileDiscoverer : IFileDiscoverer
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "System Volume Information", "$RECYCLE.BIN", "_out", "bin", "obj"
    };

    // Max path length for Win32 without extended prefix.
    private const int Win32MaxPath = 260;

    public async IAsyncEnumerable<FileCandidate> DiscoverAsync(
        ScanOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = NormalizeLongPath(Path.GetFullPath(options.SourcePath));
        if (!Directory.Exists(source))
            yield break;

        var excludedRoots = GetExcludedRoots(source, options);
        var pending = new Queue<string>();
        pending.Enqueue(source);
        var started = DateTimeOffset.UtcNow;
        var searchedDirs  = 0;
        var searchedFiles = 0;
        var matchedFiles  = 0;
        var scannedBytes  = 0L;
        var matchedBytes  = 0L;
        var matchedDirs   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxSearchedDirs  = options.MaxSearchedDirectories ?? options.MaxDirectories;
        var maxMatchedFiles  = options.MaxMatchedFiles ?? options.MaxFiles;
        var customImageRegex = CreateRegex(options.CustomImageRegex);
        var includePatterns  = CompileWildcards(options.IncludeFileNamePatterns);
        var excludePatterns  = CompileWildcards(options.ExcludeFileNamePatterns);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldStopForRuntime())
                yield break;
            if (maxSearchedDirs is int maxDirs && searchedDirs >= maxDirs)
            {
                Stop($"Stopped after visiting {maxDirs:N0} searched folders.");
                yield break;
            }

            var current = pending.Dequeue();
            searchedDirs++;

            foreach (var dir in SafeEnumerateDirectories(current))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(dir)) && !IsExcludedRoot(dir, excludedRoots))
                    pending.Enqueue(dir);
            }

            // Collect access-denied file stubs before yielding matched candidates.
            foreach (var (file, accessError) in SafeEnumerateFilesWithErrors(current))
            {
                if (accessError is not null)
                {
                    // Yield a zero-size error stub so the user sees access-denied files.
                    var ext2 = Path.GetExtension(file).ToLowerInvariant();
                    var cat2 = GetCategory(file, ext2, options, customImageRegex);
                    var stub = new FileCandidate(file, Path.GetRelativePath(source, file), ext2,
                        cat2 == MediaCategory.Unknown ? MediaCategory.Image : cat2,
                        0L, DateTimeOffset.MinValue);
                    // We surface this as an inline ValidationOutcome; create a
                    // surrogate ScanResultRecord via a dedicated error path in
                    // ScannerPipeline. For discoverer purposes we yield the stub
                    // with a flag recognised by the pipeline.
                    // Since we cannot carry error metadata on FileCandidate we
                    // yield the stub — ScannerPipeline will call the validator
                    // which will hit File.Exists → Error outcome.
                    yield return stub;
                    continue;
                }

                FileInfo info;
                try { info = new FileInfo(file); }
                catch (PathTooLongException)  { continue; }  // already long-path normalised; skip if still fails
                catch (UnauthorizedAccessException) { continue; }
                catch { continue; }

                searchedFiles++;
                scannedBytes += info.Exists ? info.Length : 0;

                if (ShouldStopForRuntime() || ShouldStopBeforeNextMatch())
                    yield break;
                if (!MatchesFileFilters(info, includePatterns, excludePatterns))
                    continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                var category = GetCategory(file, ext, options, customImageRegex);
                if (category == MediaCategory.Unknown || !IsEnabled(category, options))
                    continue;
                if (options.MinCandidateBytes is long minBytes && info.Length < minBytes)
                    continue;
                if (options.MaxCandidateBytes is long maxBytes && info.Length > maxBytes)
                    continue;

                if (options.MaxMatchedBytes is long maxMatchedBytes && matchedBytes + info.Length > maxMatchedBytes)
                {
                    Stop($"Stopped before exceeding the total matched size limit of {FormatBytes(maxMatchedBytes)}.");
                    yield break;
                }

                var matchDir = Path.GetDirectoryName(info.FullName);
                if (options.MaxMatchedDirectories is int maxMatchedDirs
                    && !string.IsNullOrWhiteSpace(matchDir)
                    && !matchedDirs.Contains(matchDir)
                    && matchedDirs.Count >= maxMatchedDirs)
                {
                    Stop($"Stopped after finding files in {maxMatchedDirs:N0} matched folders.");
                    yield break;
                }

                matchedFiles++;
                matchedBytes += info.Exists ? info.Length : 0;
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

        options.LimitState?.Stop("Source exhausted.");

        bool ShouldStopForRuntime()
        {
            if (options.MaxRuntimeSeconds is not int maxSeconds || maxSeconds <= 0)
                return false;
            if ((DateTimeOffset.UtcNow - started).TotalSeconds < maxSeconds)
                return false;
            Stop($"Stopped after reaching the {maxSeconds:N0}s time limit.");
            return true;
        }

        bool ShouldStopBeforeNextMatch()
        {
            if (options.MaxSearchedFiles is int maxSearchedFiles && searchedFiles > maxSearchedFiles)
            {
                Stop($"Stopped after inspecting {maxSearchedFiles:N0} searched files.");
                return true;
            }
            if (options.MinScannedBytes is long minScannedBytes && scannedBytes < minScannedBytes)
                return false;
            if (options.MaxScannedBytes is long maxScannedBytes && scannedBytes > maxScannedBytes)
            {
                Stop($"Stopped after inspecting {FormatBytes(maxScannedBytes)} of searched data.");
                return true;
            }
            if (maxMatchedFiles is int maxFiles && matchedFiles >= maxFiles)
            {
                Stop($"Stopped after matching {maxFiles:N0} files.");
                return true;
            }
            if (options.MinMatchedBytes is long minMatchedBytes && matchedBytes < minMatchedBytes)
                return false;
            return false;
        }

        void Stop(string reason) => options.LimitState?.Stop(reason);
    }

    /// <summary>
    /// Returns (filePath, errorMessage?) pairs. errorMessage is non-null for
    /// access-denied entries so callers can surface them as Error stubs.
    /// </summary>
    private static IEnumerable<(string Path, string? Error)> SafeEnumerateFilesWithErrors(string path)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFiles(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException ex) { yield return (path, "access_denied: " + ex.Message); yield break; }
        catch (PathTooLongException)           { yield break; }
        catch                                  { yield break; }

        foreach (var file in entries)
        {
            // Normalise long paths before yielding so downstream File.Exists etc. work.
            yield return (NormalizeLongPath(file), null);
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(NormalizeLongPath(path))
                            .Select(NormalizeLongPath)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch { return []; }
    }

    /// <summary>
    /// Prepend the Win32 long-path prefix (\\?\) on Windows when the path
    /// exceeds the default MAX_PATH limit of 260 characters.
    /// This prevents silent skip of deeply nested directories and files.
    /// </summary>
    private static string NormalizeLongPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.Length < Win32MaxPath) return path;
        // UNC paths get a different prefix.
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    private static bool IsEnabled(MediaCategory category, ScanOptions options) => category switch
    {
        MediaCategory.Image    => options.EnableImages,
        MediaCategory.Video    => options.EnableVideo,
        MediaCategory.Audio    => options.EnableAudio,
        MediaCategory.Document => options.EnableDocuments,
        _                      => false
    };

    private static MediaCategory GetCategory(string file, string extension, ScanOptions options, Regex? customImageRegex)
    {
        if (SupportedMedia.GetCategory(extension) is { } category && category != MediaCategory.Unknown)
            return category;
        if (options.CustomImageExtensions?.Contains(extension) == true)
            return MediaCategory.Image;
        return customImageRegex?.IsMatch(Path.GetFileName(file)) == true ? MediaCategory.Image : MediaCategory.Unknown;
    }

    private static Regex? CreateRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static bool MatchesFileFilters(FileInfo info, IReadOnlyList<Regex> includePatterns, IReadOnlyList<Regex> excludePatterns)
    {
        var name = info.Name;
        if (includePatterns.Count > 0 && !includePatterns.Any(x => x.IsMatch(name))) return false;
        if (excludePatterns.Any(x => x.IsMatch(name))) return false;
        return true;
    }

    private static IReadOnlyList<Regex> CompileWildcards(IReadOnlyList<string>? patterns) =>
        patterns?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => "^" + Regex.Escape(x.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$")
            .Select(x => new Regex(x, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
            .ToArray() ?? [];

    private static IReadOnlySet<string> GetExcludedRoots(string source, ScanOptions options)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfInsideSource(options.TargetRoot);
        AddIfInsideSource(options.BackupRoot);
        return roots;

        void AddIfInsideSource(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var fullPath = Path.GetFullPath(path);
            if (!PathsEqual(source, fullPath) && IsSameOrChildPath(source, fullPath))
                roots.Add(NormalizeDirectory(fullPath));
        }
    }

    private static bool IsExcludedRoot(string path, IReadOnlySet<string> excludedRoots) =>
        excludedRoots.Contains(NormalizeDirectory(path));

    private static bool IsSameOrChildPath(string parent, string path)
    {
        var normalizedParent = NormalizeDirectory(parent);
        var normalizedPath   = NormalizeDirectory(path);
        return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        NormalizeDirectory(left).Equals(NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824L) return (bytes / 1073741824d).ToString("N2") + " GB";
        if (bytes >= 1048576L)    return (bytes / 1048576d).ToString("N1") + " MB";
        if (bytes >= 1024L)       return (bytes / 1024d).ToString("N1") + " KB";
        return bytes + " B";
    }
}
