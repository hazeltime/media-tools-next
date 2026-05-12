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
            if (options.ValidationDepth == ValidationDepth.Fast)
                return Done(ValidationStatus.Valid, "header_match_fast_mode");

            var magick = tools.FindExecutable("magick");
            if (magick is null)
                return Done(ValidationStatus.Unknown, "header_match_magick_missing");

            var arguments = options.ValidationDepth == ValidationDepth.Deep
                ? new[] { "identify", "-regard-warnings", candidate.FullPath }
                : new[] { "identify", "-ping", candidate.FullPath };

            var result = await _runner.RunAsync(
                magick,
                arguments,
                TimeSpan.FromSeconds(options.ExternalToolTimeoutSeconds),
                cancellationToken);

            if (result.TimedOut) return Done(ValidationStatus.Corrupt, "timeout");
            if (result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardError))
                return Done(ValidationStatus.Valid, "header_and_magick_ok");
            return Done(ValidationStatus.Corrupt, FirstDetail(result));
        }
        catch (UnauthorizedAccessException ex) { return Done(ValidationStatus.Error, "access_denied: " + ex.Message); }
        catch (Exception ex)                   { return Done(ValidationStatus.Error, ex.GetType().Name + ": " + ex.Message); }

        ValidationOutcome Done(ValidationStatus status, string? detail) =>
            new(candidate, status, "image-header-magick", detail, sw.Elapsed);
    }

    private static string FirstDetail(ProcessResult result) =>
        !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.StandardOutput;
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

        var ffprobeArgs = new[]
        {
            "-v", "error",
            "-i", candidate.FullPath,
            "-f", "null", "-"
        };

        var timeout = TimeSpan.FromSeconds(
            Math.Max(options.ExternalToolTimeoutSeconds, options.MediaProbeSeconds));

        var result = await _runner.RunAsync(ffprobe, ffprobeArgs, timeout, cancellationToken);

        if (result.TimedOut)
            return new(candidate, ValidationStatus.Corrupt, "ffprobe", "timeout", sw.Elapsed);

        if (result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardError))
        {
            if (options.ValidationDepth != ValidationDepth.Deep || Category == MediaCategory.Audio)
                return new(candidate, ValidationStatus.Valid, "ffprobe", null, sw.Elapsed);

            var ffmpeg = tools.FindExecutable("ffmpeg");
            if (ffmpeg is null)
                return new(candidate, ValidationStatus.Valid, "ffprobe", "ffmpeg_missing_after_ffprobe_ok", sw.Elapsed);

            var deepArgs = new[]
            {
                "-v", "error",
                "-i", candidate.FullPath,
                "-t", options.MediaProbeSeconds.ToString(),
                "-f", "null", "-"
            };
            var deepTimeout = TimeSpan.FromSeconds(options.MediaProbeSeconds + options.ExternalToolTimeoutSeconds);
            var deep = await _runner.RunAsync(ffmpeg, deepArgs, deepTimeout, cancellationToken);

            if (deep.TimedOut)
                return new(candidate, ValidationStatus.Corrupt, "ffmpeg", "timeout", sw.Elapsed);

            return deep.ExitCode == 0 && string.IsNullOrWhiteSpace(deep.StandardError)
                ? new(candidate, ValidationStatus.Valid, "ffmpeg", null, sw.Elapsed)
                : new(candidate, ValidationStatus.Corrupt, "ffmpeg",
                    !string.IsNullOrWhiteSpace(deep.StandardError) ? deep.StandardError : deep.StandardOutput,
                    sw.Elapsed);
        }

        var detail = !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.StandardOutput;
        return new(candidate, ValidationStatus.Corrupt, "ffprobe", detail, sw.Elapsed);
    }
}

public sealed class DocumentValidator(IExternalToolProbe tools) : IMediaValidator
{
    private readonly ProcessRunner _runner = new();
    public MediaCategory Category => MediaCategory.Document;
    private const int ProbeBytes = 4096;

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

            var result = await _runner.RunAsync(
                qpdf,
                ["--check", candidate.FullPath],
                TimeSpan.FromSeconds(options.ExternalToolTimeoutSeconds),
                cancellationToken);

            // qpdf exit codes:
            //   0 = OK
            //   1 = warnings only (e.g. auto-repaired XRef) — treat as Unknown
            //       so repaired files are not silently classified as Valid.
            //   2 = errors
            //   3 = encrypted without password
            return result.ExitCode switch
            {
                0 => new(candidate, ValidationStatus.Valid,   "qpdf", null, sw.Elapsed),
                1 => new(candidate, ValidationStatus.Unknown, "qpdf", "qpdf_warnings: " + (result.StandardError + result.StandardOutput).Trim(), sw.Elapsed),
                3 => new(candidate, ValidationStatus.Unknown, "qpdf", "encrypted_no_password", sw.Elapsed),
                _ => new(candidate, ValidationStatus.Corrupt, "qpdf", (result.StandardError + result.StandardOutput).Trim(), sw.Elapsed)
            };
        }

        // Non-PDF: read a minimal probe block to catch truncated/corrupt files.
        try
        {
            await using var stream = File.Open(candidate.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[Math.Min(ProbeBytes, candidate.SizeBytes)];
            var read = await stream.ReadAsync(buf, cancellationToken);
            return read > 0
                ? new(candidate, ValidationStatus.Valid,   "read-probe", null, sw.Elapsed)
                : new(candidate, ValidationStatus.Corrupt, "read-probe", "zero_bytes_read", sw.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(candidate, ValidationStatus.Error,  "read-probe", "access_denied: " + ex.Message, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new(candidate, ValidationStatus.Corrupt, "read-probe", ex.Message, sw.Elapsed);
        }
    }
}
