using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ScannerPipelineFlowTests
{
    [Fact]
    public async Task ForceRescanStillValidatesCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "a.txt");
            File.WriteAllText(source, "hello");
            var db = Path.Combine(root, "state.db");
            var validator = new CountingValidator();
            var pipeline = new ScannerPipeline(
                new FileDiscoverer(),
                new ValidatorRegistry([validator]),
                new FileActionService(),
                new SqliteScanStore(db),
                new ScanControl());

            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db);
            await pipeline.RunAsync(options, null, CancellationToken.None);
            await pipeline.RunAsync(options, null, CancellationToken.None);
            await pipeline.RunAsync(options with { ForceRescan = true }, null, CancellationToken.None);

            Assert.Equal(3, validator.Calls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RepeatedRunsAlwaysProduceFreshValidationResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var db = Path.Combine(root, "state.db");
            var store = new SqliteScanStore(db);
            var pipeline = new ScannerPipeline(
                new FileDiscoverer(),
                new ValidatorRegistry([new CountingValidator()]),
                new FileActionService(),
                store,
                new ScanControl());

            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db);
            await pipeline.RunAsync(options, null, CancellationToken.None);
            var second = await pipeline.RunAsync(options, null, CancellationToken.None);
            var results = await store.ListResultsAsync(second.SessionId, CancellationToken.None);

            Assert.Equal(1, second.Valid);
            Assert.Equal(0, second.Skipped);
            Assert.Equal(ValidationStatus.Valid, results[0].Status);
            Assert.Equal("dry-run", results[0].Action);
            Assert.Null(results[0].Detail);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RuntimeLimitStopsPipelineWithPartialSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var db = Path.Combine(root, "state.db");
            var pipeline = new ScannerPipeline(
                new FileDiscoverer(),
                new ValidatorRegistry([new SlowValidator()]),
                new FileActionService(),
                new SqliteScanStore(db),
                new ScanControl());

            var summary = await pipeline.RunAsync(
                ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db) with
                {
                    MaxRuntimeSeconds = 1,
                    LimitState = new ScanLimitState()
                },
                null,
                CancellationToken.None);

            Assert.Equal("Stopped after reaching the 1s time limit.", summary.CompletionReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PipelineReturnsDiscoveryCompletionReason()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "b.txt"), "hello");
            var db = Path.Combine(root, "state.db");
            var limitState = new ScanLimitState();
            var pipeline = new ScannerPipeline(
                new FileDiscoverer(),
                new ValidatorRegistry([new CountingValidator()]),
                new FileActionService(),
                new SqliteScanStore(db),
                new ScanControl());

            var summary = await pipeline.RunAsync(
                ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db) with
                {
                    MaxMatchedFiles = 1,
                    LimitState = limitState
                },
                null,
                CancellationToken.None);

            Assert.Equal(1, summary.Total);
            Assert.Equal("Stopped after matching 1 files.", summary.CompletionReason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PipelineHonorsConfiguredMaxConcurrency()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(root, $"file-{i}.txt"), "hello");

            var db = Path.Combine(root, "state.db");
            var validator = new ConcurrencyTrackingValidator();
            var pipeline = new ScannerPipeline(
                new FileDiscoverer(),
                new ValidatorRegistry([validator]),
                new FileActionService(),
                new SqliteScanStore(db),
                new ScanControl());

            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db) with { MaxConcurrency = 1 };
            await pipeline.RunAsync(options, null, CancellationToken.None);

            Assert.Equal(1, validator.MaxObserved);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PipelineUsesConfiguredParallelismAboveOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 8; i++)
                File.WriteAllText(Path.Combine(root, $"file-{i}.txt"), "hello");

            var db = Path.Combine(root, "state.db");
            var validator = new ConcurrencyTrackingValidator();
            var pipeline = new ScannerPipeline(
                new FileDiscoverer(),
                new ValidatorRegistry([validator]),
                new FileActionService(),
                new SqliteScanStore(db),
                new ScanControl());

            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db) with { MaxConcurrency = 2 };
            await pipeline.RunAsync(options, null, CancellationToken.None);

            Assert.InRange(validator.MaxObserved, 2, 2);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CountingValidator : IMediaValidator
    {
        public int Calls { get; private set; }
        public MediaCategory Category => MediaCategory.Document;

        public Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero));
        }
    }

    private sealed class SlowValidator : IMediaValidator
    {
        public MediaCategory Category => MediaCategory.Document;

        public async Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new ValidationOutcome(candidate, ValidationStatus.Valid, "slow", null, TimeSpan.Zero);
        }
    }

    private sealed class ConcurrencyTrackingValidator : IMediaValidator
    {
        private int _active;
        private int _maxObserved;

        public MediaCategory Category => MediaCategory.Document;
        public int MaxObserved => _maxObserved;

        public async Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            int snapshot;
            do
            {
                snapshot = _maxObserved;
                if (active <= snapshot) break;
            }
            while (Interlocked.CompareExchange(ref _maxObserved, active, snapshot) != snapshot);

            try
            {
                await Task.Delay(25, cancellationToken);
                return new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
