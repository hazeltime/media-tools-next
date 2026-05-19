using MediaToolsNext.Core;
using System.Text.RegularExpressions;

namespace MediaToolsNext.Infrastructure;

public sealed class FileDiscoverer : IFileDiscoverer
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "System Volume Information", "$RECYCLE.BIN", "_out", "bin", "obj"
    };

    private const int Win32MaxPath = 260;

    private static readonly ScanDiscoveryEvent EvSearched          = new(DiscoveryEventType.Searched);
    private static readonly ScanDiscoveryEvent EvFilteredSize      = new(DiscoveryEventType.FilteredOutSize);
    private static readonly ScanDiscoveryEvent EvFilteredPattern   = new(DiscoveryEventType.FilteredOutPattern);
    private static readonly ScanDiscoveryEvent EvFilteredFamily    = new(DiscoveryEventType.FilteredOutFamily);

    public async IAsyncEnumerable<FileCandidate> DiscoverAsync(
        ScanOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        IProgress<ScanDiscoveryEvent>? discoveryProgress = null)
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
        var minRuntimeBeforeLimits = TimeSpan.FromSeconds(Math.Max(0, options.MinRuntimeBeforeLimitsSeconds));
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

            foreach (var (file, accessError) in SafeEnumerateFilesWithErrors(current))
            {
                if (accessError is not null)
                {
                    // Keep access-denied stubs out of validators; the pipeline records them explicitly.
                    var ext2 = Path.GetExtension(file).ToLowerInvariant();
                    var cat2 = GetCategory(file, ext2, options, customImageRegex);
                    var stub = new FileCandidate(
                        file,
                        Path.GetRelativePath(source, file),
                        ext2,
                        cat2 == MediaCategory.Unknown ? MediaCategory.Unknown : cat2,
                        0L,
                        DateTimeOffset.MinValue);
                    discoveryProgress?.Report(EvSearched);
                    yield return stub;
                    continue;
                }

                FileInfo info;
                try { info = new FileInfo(file); }
                catch (PathTooLongException)        { continue; }
                catch (UnauthorizedAccessException) { continue; }
                catch                               { continue; }

                var fileExists = info.Exists;
                var fileLength = fileExists ? info.Length : 0L;

                searchedFiles++;
                scannedBytes += fileLength;
                discoveryProgress?.Report(EvSearched);

                if (ShouldStopForRuntime() || ShouldStopBeforeNextMatch())
                    yield break;

                if (!MatchesFileFilters(info, includePatterns, excludePatterns))
                {
                    discoveryProgress?.Report(EvFilteredPattern);
                    continue;
                }

                var ext = Path.GetExtension(file).ToLowerInvariant();
                var category = GetCategory(file, ext, options, customImageRegex);
                if (category == MediaCategory.Unknown || !IsEnabled(category, options))
                {
                    discoveryProgress?.Report(EvFilteredFamily);
                    continue;
                }

                if (options.MinCandidateBytes is long minBytes && fileLength < minBytes)
                {
                    discoveryProgress?.Report(EvFilteredSize);
                    continue;
                }
                if (options.MaxCandidateBytes is long maxBytes && fileLength > maxBytes)
                {
                    discoveryProgress?.Report(EvFilteredSize);
                    continue;
                }

                var minMatchedBytesReached = options.MinMatchedBytes is not long minMatchedBytes || matchedBytes >= minMatchedBytes;
                var runtimeBeforeLimitsElapsed = minRuntimeBeforeLimits <= TimeSpan.Zero
                    || DateTimeOffset.UtcNow - started >= minRuntimeBeforeLimits;
                if (runtimeBeforeLimitsElapsed
                    && minMatchedBytesReached
                    && options.MaxMatchedBytes is long maxMatchedBytes
                    && matchedBytes + fileLength > maxMatchedBytes)
                {
                    Stop($"Stopped before exceeding the total matched size limit of {FormatBytes(maxMatchedBytes)}.");
                    yield break;
                }

                var matchDir = Path.GetDirectoryName(info.FullName);
                if (runtimeBeforeLimitsElapsed
                    && options.MaxMatchedDirectories is int maxMatchedDirs
                    && !string.IsNullOrWhiteSpace(matchDir)
                    && !matchedDirs.Contains(matchDir)
                    && matchedDirs.Count >= maxMatchedDirs)
                {
                    Stop($"Stopped after finding files in {maxMatchedDirs:N0} matched folders.");
                    yield break;
                }

                matchedFiles++;
                matchedBytes += fileLength;
                if (!string.IsNullOrWhiteSpace(matchDir))
                    matchedDirs.Add(matchDir);

                yield return new FileCandidate(
                    info.FullName,
                    Path.GetRelativePath(source, info.FullName),
                    ext,
                    category,
                    fileLength,
                    fileExists ? info.LastWriteTimeUtc : DateTimeOffset.MinValue);
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
            var minScannedBytesReached = options.MinScannedBytes is not long minScannedBytes || scannedBytes >= minScannedBytes;
            if (minScannedBytesReached
                && options.MaxScannedBytes is long maxScannedBytes
                && scannedBytes > maxScannedBytes)
            {
                Stop($"Stopped after inspecting {FormatBytes(maxScannedBytes)} of searched data.");
                return true;
            }
            if (options.MinRuntimeBeforeLimitsSeconds > 0 && DateTimeOffset.UtcNow - started < minRuntimeBeforeLimits)
                return false;
            if (maxMatchedFiles is int maxFiles && matchedFiles >= maxFiles)
            {
                Stop($"Stopped after matching {maxFiles:N0} files.");
                return true;
            }
            return false;
        }

        void Stop(string reason) => options.LimitState?.Stop(reason);
    }

    // C# does not allow yield inside a catch clause. This method captures the
    // error string into a local before the catch exits, then yields from normal
    // iterator flow after the try/catch.
    private static IEnumerable<(string Path, string? Error)> SafeEnumerateFilesWithErrors(string path)
    {
        IEnumerator<string>? entries = null;
        string? directoryError = null;
        try
        {
            entries = Directory.EnumerateFiles(path).GetEnumerator();
        }
        catch (UnauthorizedAccessException ex) { directoryError = "access_denied: " + ex.Message; }
        catch (PathTooLongException)           { yield break; }
        catch                                  { yield break; }

        if (directoryError is not null)
        {
            yield return (path, directoryError);
            yield break;
        }

        using (entries)
        {
            while (true)
            {
                string file;
                string? enumerationError = null;
                try
                {
                    if (!entries!.MoveNext())
                        yield break;
                    file = entries.Current;
                }
                catch (UnauthorizedAccessException ex) { enumerationError = "access_denied: " + ex.Message; file = path; }
                catch (PathTooLongException)           { yield break; }
                catch                                  { yield break; }

                if (enumerationError is not null)
                {
                    yield return (file, enumerationError);
                    yield break;
                }

                yield return (NormalizeLongPath(file), null);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        IEnumerator<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(NormalizeLongPath(path)).GetEnumerator();
        }
        catch { yield break; }

        var directories = new List<string>();
        using (entries)
        {
            while (true)
            {
                try
                {
                    if (!entries.MoveNext())
                        break;
                    directories.Add(NormalizeLongPath(entries.Current));
                }
                catch { yield break; }
            }
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
            yield return directory;
    }

    private static string NormalizeLongPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.Length < Win32MaxPath) return path;
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
        var normalizedExtension = extension.ToLowerInvariant();
        if (SupportedMedia.GetCategory(normalizedExtension) is { } category
            && category != MediaCategory.Unknown)
        {
            return IsExtensionEnabled(normalizedExtension, options) ? category : MediaCategory.Unknown;
        }
        if (options.CustomImageExtensions?.Contains(normalizedExtension) == true)
            return MediaCategory.Image;
        return customImageRegex?.IsMatch(Path.GetFileName(file)) == true ? MediaCategory.Image : MediaCategory.Unknown;
    }

    private static bool IsExtensionEnabled(string extension, ScanOptions options) =>
        options.EnabledExtensions is not { Count: > 0 } enabledExtensions
        || enabledExtensions.Contains(extension);

    private static Regex? CreateRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled); }
        catch (ArgumentException) { return null; }
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
