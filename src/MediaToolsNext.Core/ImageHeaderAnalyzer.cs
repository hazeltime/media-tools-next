using System.Text;

namespace MediaToolsNext.Core;

public static class ImageHeaderAnalyzer
{
    public static string Detect(string path)
    {
        byte[] buffer = new byte[64];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read < 2) return "unknown";

        if (IsJpeg(buffer, read)) return "jpeg";
        if (IsPng(buffer, read)) return "png";
        if (IsGif(buffer, read)) return "gif";
        if (IsBmp(buffer, read)) return "bmp";
        if (IsWebp(buffer, read)) return "webp";
        if (IsTiff(buffer, read)) return "tiff";
        if (IsIco(buffer, read)) return "ico";

        var brand = DetectIsoBmffBrand(buffer, read);
        if (brand == "heic") return "heic";
        if (brand == "avif") return "avif";

        return "unknown";
    }

    private static bool IsJpeg(byte[] b, int n) => n >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
    private static bool IsPng(byte[] b, int n) => n >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
    private static bool IsGif(byte[] b, int n) => n >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38 && (b[4] == 0x37 || b[4] == 0x39) && b[5] == 0x61;
    private static bool IsBmp(byte[] b, int n) => n >= 2 && b[0] == 0x42 && b[1] == 0x4D;
    private static bool IsWebp(byte[] b, int n) => n >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50;
    private static bool IsTiff(byte[] b, int n) => n >= 4 && ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 0x2A && b[3] == 0x00) || (b[0] == 0x4D && b[1] == 0x4D && b[2] == 0x00 && b[3] == 0x2A));
    private static bool IsIco(byte[] b, int n) => n >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x01 && b[3] == 0x00;

    private static string DetectIsoBmffBrand(byte[] b, int n)
    {
        if (n < 12) return "unknown";
        if (b[4] != 0x66 || b[5] != 0x74 || b[6] != 0x79 || b[7] != 0x70) return "unknown";
        var majorBrand = Encoding.ASCII.GetString(b, 8, 4);
        if (majorBrand is "heic" or "heix" or "heif" or "hevc" or "hevx" or "mif1" or "msf1") return "heic";
        if (majorBrand is "avif" or "avis") return "avif";
        for (var offset = 16; offset + 4 <= n; offset += 4)
        {
            var brand = Encoding.ASCII.GetString(b, offset, 4);
            if (brand is "heic" or "heix" or "heif" or "hevc" or "hevx" or "mif1" or "msf1") return "heic";
            if (brand is "avif" or "avis") return "avif";
        }
        return "unknown";
    }
}
