namespace MediaToolsNext.Core;

public static class SupportedMedia
{
    private static readonly Dictionary<string, MediaCategory> ExtensionToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", MediaCategory.Image }, { ".jpeg", MediaCategory.Image }, { ".jpe", MediaCategory.Image }, { ".jfif", MediaCategory.Image },
        { ".png", MediaCategory.Image }, { ".bmp", MediaCategory.Image }, { ".dib", MediaCategory.Image }, { ".gif", MediaCategory.Image },
        { ".webp", MediaCategory.Image }, { ".tif", MediaCategory.Image }, { ".tiff", MediaCategory.Image }, { ".heic", MediaCategory.Image },
        { ".heif", MediaCategory.Image }, { ".avif", MediaCategory.Image }, { ".ico", MediaCategory.Image }, { ".raw", MediaCategory.Image },
        { ".cr2", MediaCategory.Image }, { ".nef", MediaCategory.Image },
        { ".mp4", MediaCategory.Video }, { ".mkv", MediaCategory.Video }, { ".avi", MediaCategory.Video }, { ".mov", MediaCategory.Video },
        { ".wmv", MediaCategory.Video }, { ".webm", MediaCategory.Video }, { ".m4v", MediaCategory.Video }, { ".mts", MediaCategory.Video },
        { ".m2ts", MediaCategory.Video }, { ".ts", MediaCategory.Video }, { ".3gp", MediaCategory.Video }, { ".flv", MediaCategory.Video },
        { ".mp3", MediaCategory.Audio }, { ".wav", MediaCategory.Audio }, { ".aiff", MediaCategory.Audio }, { ".flac", MediaCategory.Audio },
        { ".ogg", MediaCategory.Audio }, { ".m4a", MediaCategory.Audio }, { ".wma", MediaCategory.Audio }, { ".aac", MediaCategory.Audio },
        { ".pdf", MediaCategory.Document }, { ".docx", MediaCategory.Document }, { ".gdoc", MediaCategory.Document },
        { ".rtf", MediaCategory.Document }, { ".txt", MediaCategory.Document }
    };

    public static MediaCategory GetCategory(string extension) =>
        ExtensionToCategory.TryGetValue(extension, out var category) ? category : MediaCategory.Unknown;

    public static IReadOnlyDictionary<string, MediaCategory> Extensions => ExtensionToCategory;

    public static IReadOnlyList<string> GetExtensions(MediaCategory category) =>
        ExtensionToCategory
            .Where(pair => pair.Value == category)
            .Select(pair => pair.Key)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
