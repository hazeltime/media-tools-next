using System.Diagnostics;
using System.ComponentModel;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ImageValidator : IMediaValidator
{
    private readonly IExternalToolProbe _tools;
    private readonly IProcessRunner _runner;

    public ImageValidator(IExternalToolProbe tools)
        : this(tools, new ProcessRunner())
    {
    }

    internal ImageValidator(IExternalToolProbe tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
    }

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
            if (options.ValidationDepth == ValidationDepth.Fast)
            {
                return header == "unknown"
                    ? Done(ValidationStatus.Unknown, "unknown_or_invalid_header")
                    : Done(ValidationStatus.Valid, "header_match_fast_mode");
            }

            var magick = _tools.FindExecutable("magick");
            if (magick is null)
            {
                return header == "unknown"
                    ? Done(ValidationStatus.Unknown, "unknown_header_magick_missing")
                    : Done(ValidationStatus.Unknown, "header_match_magick_missing");
            }

            var arguments = options.ValidationDepth == ValidationDepth.Deep
                ? new[] { "identify", "-regard-warnings", candidate.FullPath }
                : new[] { "identify", "-ping", candidate.FullPath };

            var result = await _runner.RunAsync(
                magick,
                arguments,
                TimeSpan.FromSeconds(options.ExternalToolTimeoutSeconds),
                cancellationToken);

            if (result.ToolNotFound) return Done(ValidationStatus.Unknown, "magick_missing_or_unavailable");
            if (result.TimedOut) return Done(ValidationStatus.Corrupt, "timeout");
            if (result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardError))
            {
                return Done(
                    ValidationStatus.Valid,
                    header == "unknown" ? "magick_ok_header_unknown" : "header_and_magick_ok");
            }
            return Done(ValidationStatus.Corrupt, FirstDetail(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException ex) { return Done(ValidationStatus.Error, "access_denied: " + ex.Message); }
        catch (Exception ex)                   { return Done(ValidationStatus.Error, ex.GetType().Name + ": " + ex.Message); }

        ValidationOutcome Done(ValidationStatus status, string? detail) =>
            new(candidate, status, "image-header-magick", detail, sw.Elapsed);
    }

    private static string FirstDetail(ProcessResult result) =>
        !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.StandardOutput;
}

public sealed class MediaStreamValidator : IMediaValidator
{
    private readonly IExternalToolProbe _tools;
    private readonly IProcessRunner _runner;

    public MediaStreamValidator(MediaCategory category, IExternalToolProbe tools)
        : this(category, tools, new ProcessRunner())
    {
    }

    internal MediaStreamValidator(MediaCategory category, IExternalToolProbe tools, IProcessRunner runner)
    {
        Category = category;
        _tools = tools;
        _runner = runner;
    }

    public MediaCategory Category { get; }

    public async Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var ffprobe = _tools.FindExecutable("ffprobe");
        if (ffprobe is null)
            return new(candidate, ValidationStatus.Unknown, "ffprobe", "ffprobe_missing", sw.Elapsed);

        var ffprobeArgs = new[]
        {
            "-v", "error",
            "-show_streams",
            "-show_format",
            "-of", "default=noprint_wrappers=1:nokey=1",
            candidate.FullPath
        };

        var timeout = TimeSpan.FromSeconds(
            Math.Max(options.ExternalToolTimeoutSeconds, options.MediaProbeSeconds));

        var result = await _runner.RunAsync(ffprobe, ffprobeArgs, timeout, cancellationToken);

        if (result.ToolNotFound)
            return new(candidate, ValidationStatus.Unknown, "ffprobe", "ffprobe_missing_or_unavailable", sw.Elapsed);

        if (result.TimedOut)
            return new(candidate, ValidationStatus.Corrupt, "ffprobe", "timeout", sw.Elapsed);

        if (result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardError))
        {
            if (options.ValidationDepth != ValidationDepth.Deep || Category == MediaCategory.Audio)
                return new(candidate, ValidationStatus.Valid, "ffprobe", null, sw.Elapsed);

            var ffmpeg = _tools.FindExecutable("ffmpeg");
            if (ffmpeg is null)
                // FIX: deep validation was requested but could not be performed because
                // ffmpeg is missing. Returning Valid here would be a false positive —
                // ffprobe only checked container/stream metadata, not frame-level integrity.
                // Return Unknown so the caller knows the result is inconclusive.
                return new(candidate, ValidationStatus.Unknown, "ffprobe", "ffmpeg_missing_deep_incomplete", sw.Elapsed);

            var deepArgs = new[]
            {
                "-v", "error",
                "-i", candidate.FullPath,
                "-t", options.MediaProbeSeconds.ToString(),
                "-f", "null", "-"
            };
            var deepTimeout = TimeSpan.FromSeconds(options.MediaProbeSeconds + options.ExternalToolTimeoutSeconds);
            var deep = await _runner.RunAsync(ffmpeg, deepArgs, deepTimeout, cancellationToken);

            if (deep.ToolNotFound)
                return new(candidate, ValidationStatus.Unknown, "ffmpeg", "ffmpeg_missing_or_unavailable", sw.Elapsed);

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

public sealed class DocumentValidator : IMediaValidator
{
    private readonly IExternalToolProbe _tools;
    private readonly IProcessRunner _runner;

    public DocumentValidator(IExternalToolProbe tools)
        : this(tools, new ProcessRunner())
    {
    }

    internal DocumentValidator(IExternalToolProbe tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
    }

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
            var qpdf = _tools.FindExecutable("qpdf");
            if (qpdf is null)
                return new(candidate, ValidationStatus.Unknown, "qpdf", "qpdf_missing", sw.Elapsed);

            var result = await _runner.RunAsync(
                qpdf,
                ["--check", candidate.FullPath],
                TimeSpan.FromSeconds(options.ExternalToolTimeoutSeconds),
                cancellationToken);

            if (result.ToolNotFound)
                return new(candidate, ValidationStatus.Unknown, "qpdf", "qpdf_missing_or_unavailable", sw.Elapsed);

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
            // Keep the Math.Min overload explicit if ProbeBytes is changed later.
            var buf = new byte[(int)Math.Min((long)ProbeBytes, candidate.SizeBytes)];
            var read = await stream.ReadAsync(buf, cancellationToken);
            return read > 0
                ? new(candidate, ValidationStatus.Valid,   "read-probe", null, sw.Elapsed)
                : new(candidate, ValidationStatus.Corrupt, "read-probe", "zero_bytes_read", sw.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(candidate, ValidationStatus.Error,  "read-probe", "access_denied: " + ex.Message, sw.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(candidate, ValidationStatus.Corrupt, "read-probe", ex.Message, sw.Elapsed);
        }
    }
}
