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
    private static bool IsAccessDeniedStub(FileCandidate c) =>
        c.SizeBytes == 0 && c.LastWriteTimeUtc == DateTimeOffset.MinValue;

    public async Task<ScanSummary> RunAsync(
        ScanOptions options,
        IProgress<ScanResultRecord>? progress,
        CancellationToken cancellationToken,
        IProgress<ScanDiscoveryEvent>? discoveryProgress = null)
    {
        using var runtimeCts = CreateRuntimeCancellation(options, cancellationToken);
        var runToken = runtimeCts?.Token ?? cancellationToken;
        var maxConcurrency = Math.Clamp(options.MaxConcurrency, 1, 32);

        await store.InitializeAsync(runToken);
        var sessionId = await store.CreateSessionAsync(options, runToken);

        var channel = Channel.CreateBounded<FileCandidate>(new BoundedChannelOptions(Math.Max(16, maxConcurrency * 4))
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var candidate in discoverer.DiscoverAsync(options, runToken, discoveryProgress))
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

        const int BatchSize = 32;

        var workers = Enumerable.Range(0, maxConcurrency).Select(_ => Task.Run(async () =>
        {
            var buffer = new List<ScanResultRecord>(BatchSize);

            async Task FlushAsync(CancellationToken ct)
            {
                if (buffer.Count == 0) return;
                await store.BatchSaveResultsAsync(buffer, ct);
                foreach (var r in buffer) progress?.Report(r);
                buffer.Clear();
            }

            try
            {
                await foreach (var candidate in channel.Reader.ReadAllAsync(runToken))
                {
                    await control.WaitIfPausedAsync(runToken);
                    var record = await ProcessCandidateAsync(sessionId, candidate, options, runToken);
                    buffer.Add(record);
                    if (buffer.Count >= BatchSize)
                        await FlushAsync(runToken);
                }
            }
            finally
            {
                await FlushAsync(CancellationToken.None);
            }
        }, runToken)).ToArray();

        try
        {
            await producer;
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            options.LimitState?.StopAfterWorkStarted(RuntimeStopReason(options));
        }

        var summary = await store.GetSummaryAsync(sessionId, cancellationToken);
        return summary with { CompletionReason = options.LimitState?.StopReason ?? summary.CompletionReason };
    }

    private async Task<ScanResultRecord> ProcessCandidateAsync(Guid sessionId, FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        var validator = IsAccessDeniedStub(candidate) ? null : validators.GetValidator(candidate.Category);
        var outcome = IsAccessDeniedStub(candidate)
            ? new ValidationOutcome(candidate, ValidationStatus.Error, "discovery", "access_denied", TimeSpan.Zero)
            : validator is not null
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
            if (attempt < options.MaxRetries)
                await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
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
            catch (MoveDeleteFailedException ex)
            {
                return new FileActionOutcome(
                    "move-delete-failed: " + ex.InnerException?.GetType().Name + ": " + ex.InnerException?.Message,
                    ex.PrimaryTargetPath,
                    ex.BackupTargetPath,
                    null);
            }
            catch (Exception ex)
            {
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
