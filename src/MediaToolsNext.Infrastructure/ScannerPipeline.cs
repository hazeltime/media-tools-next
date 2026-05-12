using System.Collections.Concurrent;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScannerPipeline(
    IFileDiscoverer discoverer,
    IValidatorRegistry validators,
    IFileActionService actions,
    IScanStore store) : IScannerPipeline
{
    public async Task<ScanSummary> RunAsync(ScanOptions options, IProgress<ScanResultRecord>? progress, CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        var sessionId = await store.CreateSessionAsync(options, cancellationToken);
        var candidates = new List<FileCandidate>();
        await foreach (var candidate in discoverer.DiscoverAsync(options, cancellationToken))
            candidates.Add(candidate);

        using var throttler = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        var tasks = candidates.Select(async candidate =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
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
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
        return await store.GetSummaryAsync(sessionId, cancellationToken);
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
