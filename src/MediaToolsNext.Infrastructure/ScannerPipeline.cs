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
    public async Task<ScanSummary> RunAsync(ScanOptions options, IProgress<ScanResultRecord>? progress, CancellationToken cancellationToken)
    {
        // Build a linked token that also cancels on MaxRuntimeSeconds expiry.
        // All operations inside this method use runToken so that the timeout
        // applies uniformly, including store initialisation and session creation.
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

        var workers = Enumerable.Range(0, Math.Max(1, options.MaxConcurrency)).Select(_ => Task.Run(async () =>
        {
            await foreach (var candidate in channel.Reader.ReadAllAsync(runToken))
            {
                await control.WaitIfPausedAsync(runToken);
                await ProcessCandidateAsync(sessionId, candidate, options, progress, runToken);
            }
        }, runToken)).ToArray();

        try
        {
            await producer;
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            options.LimitState?.StopAfterWorkStarted(RuntimeStopReason(options));
        }

        // Use the outer cancellationToken for the final summary read so we can
        // still retrieve results even if the runtime limit was reached.
        var summary = await store.GetSummaryAsync(sessionId, cancellationToken);
        return summary with { CompletionReason = options.LimitState?.StopReason ?? summary.CompletionReason };
    }

    private async Task ProcessCandidateAsync(Guid sessionId, FileCandidate candidate, ScanOptions options, IProgress<ScanResultRecord>? progress, CancellationToken cancellationToken)
    {
        var reusable = options.ForceRescan ? null : await store.FindReusableResultAsync(candidate, cancellationToken);
        if (reusable is not null)
        {
            var resumed = new ScanResultRecord(sessionId, candidate, reusable.Status, reusable.Validator, "cache_reused_previous_validation", "cached", null, null, DateTimeOffset.UtcNow);
            await store.SaveResultAsync(resumed, cancellationToken);
            progress?.Report(resumed);
            return;
        }

        var validator = validators.GetValidator(candidate.Category);
        var outcome = validator is not null
            ? await ValidateWithRetryAsync(validator, candidate, options, cancellationToken)
            : new ValidationOutcome(candidate, ValidationStatus.Skipped, "none", "category_disabled", TimeSpan.Zero);
        var action = await ApplyWithRetryAsync(outcome, options, cancellationToken);
        var record = new ScanResultRecord(sessionId, candidate, outcome.Status, outcome.Validator, outcome.Detail, action.Action, action.PrimaryTargetPath, action.BackupTargetPath, DateTimeOffset.UtcNow);
        await store.SaveResultAsync(record, cancellationToken);
        progress?.Report(record);
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
            try { return await actions.ApplyAsync(outcome, options, cancellationToken); }
            catch (IOException) when (attempt < options.MaxRetries) { await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken); }
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
