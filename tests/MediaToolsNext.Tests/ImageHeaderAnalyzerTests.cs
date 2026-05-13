using MediaToolsNext.Core;

namespace MediaToolsNext.Tests;

public class ImageHeaderAnalyzerTests
{
    [Fact]
    public void DetectsPngHeader()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            Assert.Equal("png", ImageHeaderAnalyzer.Detect(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DetectsHeifMajorBrand()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [
                0x00, 0x00, 0x00, 0x18,
                0x66, 0x74, 0x79, 0x70,
                0x68, 0x65, 0x69, 0x66,
                0x00, 0x00, 0x00, 0x00
            ]);

            Assert.Equal("heic", ImageHeaderAnalyzer.Detect(path));
        }
        finally { File.Delete(path); }
    }
}
