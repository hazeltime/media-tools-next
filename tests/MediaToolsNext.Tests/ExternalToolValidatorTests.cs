using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ExternalToolValidatorTests
{
    [Fact]
    public async Task MediaStreamValidatorTreatsUnavailableFfprobeAsUnknown()
    {
        var candidate = Candidate("clip.mp4", MediaCategory.Video);
        var validator = new MediaStreamValidator(
            MediaCategory.Video,
            new FakeToolProbe(("ffprobe", "ffprobe")),
            new FakeProcessRunner(new ProcessResult(-1, string.Empty, "missing", false, ToolNotFound: true)));

        var outcome = await validator.ValidateAsync(candidate, Options(ValidationDepth.Standard), CancellationToken.None);

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal("ffprobe_missing_or_unavailable", outcome.Detail);
    }

    [Fact]
    public async Task DeepMediaStreamValidatorTreatsUnavailableFfmpegAsUnknown()
    {
        var candidate = Candidate("clip.mp4", MediaCategory.Video);
        var runner = new FakeProcessRunner(
            new ProcessResult(0, string.Empty, string.Empty, false),
            new ProcessResult(-1, string.Empty, "missing", false, ToolNotFound: true));
        var validator = new MediaStreamValidator(
            MediaCategory.Video,
            new FakeToolProbe(("ffprobe", "ffprobe"), ("ffmpeg", "ffmpeg")),
            runner);

        var outcome = await validator.ValidateAsync(candidate, Options(ValidationDepth.Deep), CancellationToken.None);

        Assert.Equal(ValidationStatus.Unknown, outcome.Status);
        Assert.Equal("ffmpeg_missing_or_unavailable", outcome.Detail);
    }

    [Fact]
    public async Task DocumentValidatorTreatsUnavailableQpdfAsUnknown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.7");
        try
        {
            var candidate = new FileCandidate(path, Path.GetFileName(path), ".pdf", MediaCategory.Document, 8, DateTimeOffset.UtcNow);
            var validator = new DocumentValidator(
                new FakeToolProbe(("qpdf", "qpdf")),
                new FakeProcessRunner(new ProcessResult(-1, string.Empty, "missing", false, ToolNotFound: true)));

            var outcome = await validator.ValidateAsync(candidate, Options(ValidationDepth.Standard), CancellationToken.None);

            Assert.Equal(ValidationStatus.Unknown, outcome.Status);
            Assert.Equal("qpdf_missing_or_unavailable", outcome.Detail);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static FileCandidate Candidate(string name, MediaCategory category)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        return new FileCandidate(path, name, Path.GetExtension(name), category, 1024, DateTimeOffset.UtcNow);
    }

    private static ScanOptions Options(ValidationDepth depth) =>
        new(
            SourcePath: Path.GetTempPath(),
            TargetRoot: Path.GetTempPath(),
            BackupRoot: null,
            ActionMode: ScanActionMode.DryRun,
            EnableImages: true,
            EnableVideo: true,
            EnableAudio: true,
            EnableDocuments: true,
            MaxConcurrency: 8,
            MediaProbeSeconds: 120,
            DatabasePath: Path.Combine(Path.GetTempPath(), "media-tools-next-tests.db"),
            ValidationDepth: depth,
            ExternalToolTimeoutSeconds: 15);

    private sealed class FakeToolProbe(params (string Command, string Path)[] tools) : IExternalToolProbe
    {
        private readonly Dictionary<string, string> _tools = tools.ToDictionary(x => x.Command, x => x.Path, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ToolStatus> GetStatuses() => [];

        public string? FindExecutable(string commandName) =>
            _tools.GetValueOrDefault(commandName);
    }

    private sealed class FakeProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private int _index;

        public Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(_index++, results.Length - 1);
            return Task.FromResult(results[index]);
        }
    }
}
