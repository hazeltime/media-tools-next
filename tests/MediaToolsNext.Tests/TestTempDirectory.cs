namespace MediaToolsNext.Tests;

internal sealed class TestTempDirectory : IDisposable
{
    private TestTempDirectory(string path)
    {
        Path = path;
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public static TestTempDirectory Create(string prefix = "media-tools-next-") =>
        new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid()));

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
