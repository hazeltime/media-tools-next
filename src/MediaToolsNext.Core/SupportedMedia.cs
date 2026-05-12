namespace MediaToolsNext.Core;

public static class SupportedMedia
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".webp",
        ".tif", ".tiff", ".heic", ".heif", ".avif", ".ico", ".raw", ".cr2", ".nef"
    };

    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mts", ".m2ts", ".ts", ".3gp", ".flv"
    };

    public static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".aiff", ".flac", ".ogg", ".m4a", ".wma", ".aac"
    };

    public static readonly IReadOnlySet<string> DocumentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".gdoc", ".rtf", ".txt"
    };

    public static MediaCategory GetCategory(string extension)
    {
        if (ImageExtensions.Contains(extension)) return MediaCategory.Image;
        if (VideoExtensions.Contains(extension)) return MediaCategory.Video;
        if (AudioExtensions.Contains(extension)) return MediaCategory.Audio;
        if (DocumentExtensions.Contains(extension)) return MediaCategory.Document;
        return MediaCategory.Unknown;
    }
}

