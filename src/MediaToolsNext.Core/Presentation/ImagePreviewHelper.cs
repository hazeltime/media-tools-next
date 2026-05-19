using System.IO;
using System.Threading.Tasks;
using System;

namespace MediaToolsNext.Core.Presentation;

public static class ImagePreviewHelper
{
    public const long MaxPreviewSizeBytes = 10 * 1024 * 1024; // 10MB

    public static string ResolveMimeType(string? extension)
    {
        return (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" or ".dib" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    public static bool CanPreview(long sizeBytes)
    {
        return sizeBytes <= MaxPreviewSizeBytes;
    }

    public static async Task<string?> GetBase64DataUriAsync(string fullPath, string? extension)
    {
        if (!File.Exists(fullPath))
            return null;

        var size = new FileInfo(fullPath).Length;
        if (!CanPreview(size))
            return null;

        var bytes = await File.ReadAllBytesAsync(fullPath);
        var mime = ResolveMimeType(extension);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
