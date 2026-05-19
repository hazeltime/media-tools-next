using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ImageValidatorTests
{
    [Fact]
    public async Task FastModeReturnsValidForKnownHeaderWithoutMagick()
    {
        await using var image = await TempImageFile.CreateAsync(".png", PngHeader);
        var probe = new FakeToolProbe();
        var runner = new FakeProcessRunner();
        var validator = new ImageValidator(probe, runner);

        var outcome = await validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Fast), CancellationToken.None);

        Assert.Equal(ValidationStatus.Valid, outcome.Status);
        Assert.Equal("header_match_fast_mode", outcome.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task FastModeReturnsUnknownForUnknownHeaderWithoutMagick()
    {
        await using var image = await TempImageFile.CreateAsync(".raw", UnknownHeader);
        var probe = new FakeToolProbe();
        var runner = new FakeProcessRunner();
        var validator = new ImageValidator(probe, runner);

        var outcome = await validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Fast), CancellationToken.None);

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal("unknown_or_invalid_header", outcome.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task StandardModeLetsMagickValidateUnknownImageHeader()
    {
        await using var image = await TempImageFile.CreateAsync(".raw", UnknownHeader);
        var runner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty, false));
        var validator = new ImageValidator(new FakeToolProbe("magick"), runner);

        var outcome = await validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Standard), CancellationToken.None);

        Assert.Equal(ValidationStatus.Valid, outcome.Status);
        Assert.Equal("magick_ok_header_unknown", outcome.Detail);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("magick", call.FileName);
        Assert.Equal(["identify", "-ping", image.Candidate.FullPath], call.Arguments);
    }

    [Fact]
    public async Task DeepModeUsesRegardWarningsArgument()
    {
        await using var image = await TempImageFile.CreateAsync(".jpg", JpegHeader);
        var runner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty, false));
        var validator = new ImageValidator(new FakeToolProbe("magick"), runner);

        var outcome = await validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Deep), CancellationToken.None);

        Assert.Equal(ValidationStatus.Valid, outcome.Status);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(["identify", "-regard-warnings", image.Candidate.FullPath], call.Arguments);
    }

    [Fact]
    public async Task MissingMagickAfterProbeIsUnknownNotCorrupt()
    {
        await using var image = await TempImageFile.CreateAsync(".png", PngHeader);
        var runner = new FakeProcessRunner(new ProcessResult(-1, string.Empty, "tool_not_found", false, ToolNotFound: true));
        var validator = new ImageValidator(new FakeToolProbe("magick"), runner);

        var outcome = await validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Standard), CancellationToken.None);

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal("magick_missing_or_unavailable", outcome.Detail);
    }

    [Fact]
    public async Task StandardModeWithoutMagickReturnsUnknown()
    {
        await using var image = await TempImageFile.CreateAsync(".raw", UnknownHeader);
        var runner = new FakeProcessRunner();
        var validator = new ImageValidator(new FakeToolProbe(), runner);

        var outcome = await validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Standard), CancellationToken.None);

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal("unknown_header_magick_missing", outcome.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task CancellationIsPropagatedFromRunner()
    {
        await using var image = await TempImageFile.CreateAsync(".png", PngHeader);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var validator = new ImageValidator(
            new FakeToolProbe("magick"),
            new FakeProcessRunner(exception: new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => validator.ValidateAsync(image.Candidate, Options(ValidationDepth.Standard), cancellation.Token));
    }

    private static ScanOptions Options(ValidationDepth depth) =>
        new(
            SourcePath: Path.GetTempPath(),
            TargetRoot: Path.GetTempPath(),
            BackupRoot: null,
            ActionMode: ScanActionMode.DryRun,
            EnableImages: true,
            EnableVideo: false,
            EnableAudio: false,
            EnableDocuments: false,
            MaxConcurrency: 8,
            MediaProbeSeconds: 120,
            ValidationDepth: depth,
            ExternalToolTimeoutSeconds: 15);

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] UnknownHeader = [0x52, 0x41, 0x57, 0x2D, 0x54, 0x45, 0x53, 0x54];

    private sealed class FakeToolProbe(string? magickPath = null) : IExternalToolProbe
    {
        public IReadOnlyList<ToolStatus> GetStatuses() => [];

        public string? FindExecutable(string commandName) =>
            commandName.Equals("magick", StringComparison.OrdinalIgnoreCase) ? magickPath : null;
    }

    private sealed class FakeProcessRunner(ProcessResult? result = null, Exception? exception = null) : IProcessRunner
    {
        public List<ProcessCall> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ProcessCall(fileName, arguments.ToArray(), timeout));
            if (exception is not null)
                return Task.FromException<ProcessResult>(exception);
            return Task.FromResult(result ?? new ProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed record ProcessCall(string FileName, string[] Arguments, TimeSpan Timeout);

    private sealed class TempImageFile : IAsyncDisposable
    {
        private TempImageFile(string path, FileCandidate candidate)
        {
            Path = path;
            Candidate = candidate;
        }

        private string Path { get; }
        public FileCandidate Candidate { get; }

        public static async Task<TempImageFile> CreateAsync(string extension, byte[] bytes)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(path, bytes);

            var candidate = new FileCandidate(
                path,
                System.IO.Path.GetFileName(path),
                extension,
                MediaCategory.Image,
                bytes.Length,
                DateTimeOffset.UtcNow);

            return new TempImageFile(path, candidate);
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(Path))
                File.Delete(Path);

            return ValueTask.CompletedTask;
        }
    }
}
