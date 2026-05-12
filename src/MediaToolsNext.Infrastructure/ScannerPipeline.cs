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
        await store.InitializeAsync(cancellationToken);
        var sessionId = await store.CreateSessionAsync(options, cancellationToken);
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
                await foreach (var candidate in discoverer.DiscoverAsync(options, cancellationToken))
                    await channel.Writer.WriteAsync(candidate, cancellationToken);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        var workers = Enumerable.Range(0, Math.Max(1, options.MaxConcurrency)).Select(_ => Task.Run(async () =>
        {
            await foreach (var candidate in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await control.WaitIfPausedAsync(cancellationToken);
                await ProcessCandidateAsync(sessionId, candidate, options, progress, cancellationToken);
            }
        }, cancellationToken)).ToArray();

        await producer;
        await Task.WhenAll(workers);
        return await store.GetSummaryAsync(sessionId, cancellationToken);
    }

    private async Task ProcessCandidateAsync(Guid sessionId, FileCandidate candidate, ScanOptions options, IProgress<ScanResultRecord>? progress, CancellationToken cancellationToken)
    {
        var reusable = await store.FindReusableResultAsync(candidate, cancellationToken);
        if (reusable is not null)
        {
            var resumed = new ScanResultRecord(sessionId, candidate, ValidationStatus.Skipped, reusable.Validator, "resume_unchanged: " + reusable.Status, "skipped", null, null, DateTimeOffset.UtcNow);
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
}
