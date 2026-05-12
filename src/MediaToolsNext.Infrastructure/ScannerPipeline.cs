using System.Collections.Concurrent;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ScannerPipeline(
    IFileDiscoverer discoverer,
    IEnumerable<IMediaValidator> validators,
    IFileActionService actions,
    IScanStore store) : IScannerPipeline
{
    private readonly IReadOnlyDictionary<MediaCategory, IMediaValidator> _validators = validators.ToDictionary(x => x.Category);

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
                var outcome = _validators.TryGetValue(candidate.Category, out var validator)
                    ? await validator.ValidateAsync(candidate, options, cancellationToken)
                    : new ValidationOutcome(candidate, ValidationStatus.Skipped, "none", "category_disabled", TimeSpan.Zero);
                var action = await actions.ApplyAsync(outcome, options, cancellationToken);
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
}

