using System.Threading.Channels;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScannerPipeline(
    IFileDiscoverer discoverer,
    IValidatorRegistry validators,
    IFileActionService actions,
    IScanStore store,
    ScanControl control) : IScannerPipeline
{
    // Access-denied stubs from FileDiscoverer carry SizeBytes=0 and
    // LastWriteTimeUtc=DateTimeOffset.MinValue. We identify them by this
    // sentinel so we can skip FindReusableResultAsync and avoid duplicate
    // DB rows when the same inaccessible path appears more than once.
    private static bool IsAccessDeniedStub(FileCandidate c) =>
        c.SizeBytes == 0 && c.LastWriteTimeUtc == DateTimeOffset.MinValue;

    public async Task<ScanSummary> RunAsync(ScanOptions options, IProgress<ScanResultRecord>? progress, CancellationToken cancellationToken)
    {
        using var runtimeCts = CreateRuntimeCancellation(options, cancellationToken);
        var runToken = runtimeCts?.Token ?? cancellationToken;

        await store.InitializeAsync(runToken);
        var sessionId = await store.CreateSessionAsync(options, runToken);

        var channel = Channel.CreateBounded<FileCandidate>(new BoundedChannelOptions(Math.Max(16, options.MaxConcurrency * 4))
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var candidate in discoverer.DiscoverAsync(options, runToken))
                    await channel.Writer.WriteAsync(candidate, runToken);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                options.LimitState?.StopAfterWorkStarted(RuntimeStopReason(options));
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, runToken);

        // Per-worker result buffer: flush to DB every BatchSize records to
        // amortise SQLite open/close overhead without holding large batches.
        const int BatchSize = 32;

        var workers = Enumerable.Range(0, Math.Max(1, options.MaxConcurrency)).Select(_ => Task.Run(async () =>
        {
            var buffer = new List<ScanResultRecord>(BatchSize);

            async Task FlushAsync(CancellationToken ct)
            {
                if (buffer.Count == 0) return;
                await store.BatchSaveResultsAsync(buffer, ct);
                foreach (var r in buffer) progress?.Report(r);
                buffer.Clear();
            }

            await foreach (var candidate in channel.Reader.ReadAllAsync(runToken))
            {
                await control.WaitIfPausedAsync(runToken);
                var record = await ProcessCandidateAsync(sessionId, candidate, options, runToken);
                buffer.Add(record);
                if (buffer.Count >= BatchSize)
                    await FlushAsync(runToken);
            }

            await FlushAsync(runToken);
        }, runToken)).ToArray();

        try
        {
            await producer;
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            // Covers both user cancel and runtime timeout. Always update LimitState
            // so the UI shows the correct stop reason regardless of which token fired.
            options.LimitState?.StopAfterWorkStarted(RuntimeStopReason(options));
        }

        var summary = await store.GetSummaryAsync(sessionId, cancellationToken);
        return summary with { CompletionReason = options.LimitState?.StopReason ?? summary.CompletionReason };
    }

    private async Task<ScanResultRecord> ProcessCandidateAsync(Guid sessionId, FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        // Access-denied stubs must not go through cache lookup — they have a
        // synthetic key (size=0, time=MinValue) that would match any previously
        // stored zero-byte entry for the same path.
        if (!IsAccessDeniedStub(candidate))
        {
            var reusable = options.ForceRescan ? null : await store.FindReusableResultAsync(candidate, cancellationToken);
            if (reusable is not null)
            {
                // Preserve the original detail from the cached result so the user
                // can see *why* the file got its status, appended with (cached).
                var originalDetail = string.IsNullOrWhiteSpace(reusable.Detail)
                    ? "(cached)"
                    : reusable.Detail + " (cached)";
                var cached = new ScanResultRecord(sessionId, candidate, reusable.Status, reusable.Validator,
                    originalDetail, "cached", null, null, DateTimeOffset.UtcNow);
                await store.BatchSaveResultsAsync([cached], cancellationToken);
                return cached;
            }
        }

        var validator = validators.GetValidator(candidate.Category);
        var outcome = validator is not null
            ? await ValidateWithRetryAsync(validator, candidate, options, cancellationToken)
            : new ValidationOutcome(candidate, ValidationStatus.Skipped, "none", "category_disabled", TimeSpan.Zero);
        var action = await ApplyWithRetryAsync(outcome, options, cancellationToken);
        return new ScanResultRecord(sessionId, candidate, outcome.Status, outcome.Validator, outcome.Detail, action.Action, action.PrimaryTargetPath, action.BackupTargetPath, DateTimeOffset.UtcNow);
    }

    private static async Task<ValidationOutcome> ValidateWithRetryAsync(IMediaValidator validator, FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        ValidationOutcome? last = null;
        for (var attempt = 0; attempt <= Math.Max(0, options.MaxRetries); attempt++)
        {
            last = await validator.ValidateAsync(candidate, options, cancellationToken);
            if (!RetryPolicy.ShouldRetry(last)) return last;
            if (attempt < options.MaxRetries) await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
        }
        return last!;
    }

    private async Task<FileActionOutcome> ApplyWithRetryAsync(ValidationOutcome outcome, ScanOptions options, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await actions.ApplyAsync(outcome, options, cancellationToken);
            }
            catch (Exception ex) when (RetryPolicy.ShouldRetryIOException(ex) && attempt < options.MaxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
            }
            catch (Exception ex)
            {
                // BUG FIX: previously only non-retryable exceptions were caught here.
                // If ShouldRetryIOException==true but attempt>=MaxRetries the exception
                // would escape the loop uncaught, propagating out of the worker task and
                // crashing the scan session. Now we always land here after retries are
                // exhausted — or immediately for non-retryable exceptions.
                return new FileActionOutcome("error: " + ex.GetType().Name + ": " + ex.Message, null, null, null);
            }
        }
    }

    private static CancellationTokenSource? CreateRuntimeCancellation(ScanOptions options, CancellationToken cancellationToken)
    {
        if (options.MaxRuntimeSeconds is not int seconds || seconds <= 0)
            return null;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    private static string RuntimeStopReason(ScanOptions options) =>
        options.MaxRuntimeSeconds is int seconds && seconds > 0
            ? $"Stopped after reaching the {seconds:N0}s time limit."
            : "Cancelled.";
}
