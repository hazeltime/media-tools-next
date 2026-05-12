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
}

