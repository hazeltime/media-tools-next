using System.Diagnostics;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ImageValidator(IExternalToolProbe tools) : IMediaValidator
{
    private readonly ProcessRunner _runner = new();
    public MediaCategory Category => MediaCategory.Image;

    public async Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!File.Exists(candidate.FullPath))
                return Done(ValidationStatus.Error, "missing_during_scan");
            if (candidate.SizeBytes == 0)
                return Done(ValidationStatus.Corrupt, "zero_byte");

            var header = ImageHeaderAnalyzer.Detect(candidate.FullPath);
            if (header == "unknown")
                return Done(ValidationStatus.Unknown, "unknown_or_invalid_header");

            var magick = tools.FindExecutable("magick");
            if (magick is null)
                return Done(ValidationStatus.Valid, "header_match_magick_missing");

            var result = await _runner.RunAsync(magick, ["identify", "-ping", candidate.FullPath], TimeSpan.FromSeconds(20), cancellationToken);
            if (result.TimedOut) return Done(ValidationStatus.Corrupt, "timeout");
            if (result.ExitCode == 0) return Done(ValidationStatus.Valid, "header_and_magick_ok");
            return Done(ValidationStatus.Corrupt, FirstDetail(result));
        }
        catch (UnauthorizedAccessException ex) { return Done(ValidationStatus.Error, "access_denied: " + ex.Message); }
        catch (Exception ex) { return Done(ValidationStatus.Error, ex.GetType().Name + ": " + ex.Message); }

        ValidationOutcome Done(ValidationStatus status, string? detail) =>
            new(candidate, status, "image-header-magick", detail, sw.Elapsed);
    }

    private static string FirstDetail(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
}

public sealed class MediaStreamValidator(MediaCategory category, IExternalToolProbe tools) : IMediaValidator
{
    private readonly ProcessRunner _runner = new();
    public MediaCategory Category { get; } = category;

    public async Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var ffprobe = tools.FindExecutable("ffprobe");
        if (ffprobe is null)
            return new(candidate, ValidationStatus.Unknown, "ffprobe", "ffprobe_missing", sw.Elapsed);

        var result = await _runner.RunAsync(
            ffprobe,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", candidate.FullPath],
            TimeSpan.FromSeconds(Math.Max(10, options.MediaProbeSeconds)),
            cancellationToken);

        if (result.TimedOut)
            return new(candidate, ValidationStatus.Corrupt, "ffprobe", "timeout", sw.Elapsed);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            return new(candidate, ValidationStatus.Valid, "ffprobe", null, sw.Elapsed);
        return new(candidate, ValidationStatus.Corrupt, "ffprobe", string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError, sw.Elapsed);
    }
}

public sealed class DocumentValidator(IExternalToolProbe tools) : IMediaValidator
{
    private readonly ProcessRunner _runner = new();
    public MediaCategory Category => MediaCategory.Document;

    public async Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(candidate.FullPath))
            return new(candidate, ValidationStatus.Error, "document", "missing_during_scan", sw.Elapsed);
        if (candidate.SizeBytes == 0)
            return new(candidate, ValidationStatus.Corrupt, "document", "zero_byte", sw.Elapsed);

        if (candidate.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var qpdf = tools.FindExecutable("qpdf");
            if (qpdf is null)
                return new(candidate, ValidationStatus.Unknown, "qpdf", "qpdf_missing", sw.Elapsed);

            var result = await _runner.RunAsync(qpdf, ["--check", candidate.FullPath], TimeSpan.FromSeconds(20), cancellationToken);
            return result.ExitCode == 0
                ? new(candidate, ValidationStatus.Valid, "qpdf", null, sw.Elapsed)
                : new(candidate, ValidationStatus.Corrupt, "qpdf", result.StandardError + result.StandardOutput, sw.Elapsed);
        }

        try
        {
            using var stream = File.Open(candidate.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new(candidate, ValidationStatus.Valid, "read-open", null, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new(candidate, ValidationStatus.Corrupt, "read-open", ex.Message, sw.Elapsed);
        }
    }
}

