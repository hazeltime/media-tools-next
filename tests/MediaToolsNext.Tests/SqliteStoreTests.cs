using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class SqliteStoreTests
{
    [Fact]
    public async Task StoresAndSummarizesResults()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        await store.InitializeAsync(CancellationToken.None);
        var session = await store.CreateSessionAsync(options, CancellationToken.None);
        var candidate = new FileCandidate(Path.Combine(temp.Path, "a.txt"), "a.txt", ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
        await store.SaveResultAsync(new ScanResultRecord(session, candidate, ValidationStatus.Valid, "test", null, "dry-run", null, null, DateTimeOffset.UtcNow), CancellationToken.None);
        var summary = await store.GetSummaryAsync(session, CancellationToken.None);
        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.Valid);
    }

    [Fact]
    public async Task StoresPriorResultsForHistoryOnly()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        await store.InitializeAsync(CancellationToken.None);
        var session = await store.CreateSessionAsync(options, CancellationToken.None);
        var candidate = new FileCandidate(Path.Combine(temp.Path, "a.txt"), "a.txt", ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
        await store.SaveResultAsync(new ScanResultRecord(session, candidate, ValidationStatus.Valid, "test", null, "dry-run", null, null, DateTimeOffset.UtcNow), CancellationToken.None);
        var results = await store.ListResultsAsync(session, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(ValidationStatus.Valid, results[0].Status);
    }

    [Fact]
    public async Task BatchSaveResultsStoresAndSummarizesAllRows()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        await store.InitializeAsync(CancellationToken.None);
        var session = await store.CreateSessionAsync(options, CancellationToken.None);
        var records = new[]
        {
            CreateRecord(session, temp.Path, "valid.txt", ValidationStatus.Valid),
            CreateRecord(session, temp.Path, "corrupt.txt", ValidationStatus.Corrupt),
            CreateRecord(session, temp.Path, "error.txt", ValidationStatus.Error),
            CreateRecord(session, temp.Path, "unknown.txt", ValidationStatus.Unknown),
            CreateRecord(session, temp.Path, "skipped.txt", ValidationStatus.Skipped)
        };

        await store.BatchSaveResultsAsync(records, CancellationToken.None);

        var listed = await store.ListResultsAsync(session, CancellationToken.None);
        var summary = await store.GetSummaryAsync(session, CancellationToken.None);
        Assert.Equal(5, listed.Count);
        Assert.Equal(5, summary.Total);
        Assert.Equal(1, summary.Valid);
        Assert.Equal(1, summary.Corrupt);
        Assert.Equal(1, summary.Errors);
        Assert.Equal(1, summary.Unknown);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task ListsRecentSessions()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        await store.InitializeAsync(CancellationToken.None);
        var session = await store.CreateSessionAsync(options, CancellationToken.None);

        var sessions = await store.ListSessionsAsync(10, CancellationToken.None);

        Assert.Contains(sessions, x => x.SessionId == session);
        Assert.Equal(temp.Path, sessions.Single(x => x.SessionId == session).SourcePath);
    }

    [Fact]
    public async Task ListSessionsReturnsNewestFirstAndHonorsTake()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        await store.InitializeAsync(CancellationToken.None);
        var oldest = await store.CreateSessionAsync(options, CancellationToken.None);
        var middle = await store.CreateSessionAsync(options, CancellationToken.None);
        var newest = await store.CreateSessionAsync(options, CancellationToken.None);

        var sessions = await store.ListSessionsAsync(2, CancellationToken.None);

        Assert.Equal([newest, middle], sessions.Select(x => x.SessionId).ToArray());
        Assert.DoesNotContain(sessions, x => x.SessionId == oldest);
    }

    [Fact]
    public async Task ListResultsPreservesInsertionOrderForEqualTimestamps()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        var timestamp = DateTimeOffset.Parse("2026-05-19T12:00:00Z");
        await store.InitializeAsync(CancellationToken.None);
        var session = await store.CreateSessionAsync(options, CancellationToken.None);
        var records = new[]
        {
            CreateRecord(session, temp.Path, "first.txt", ValidationStatus.Valid, timestamp),
            CreateRecord(session, temp.Path, "second.txt", ValidationStatus.Valid, timestamp),
            CreateRecord(session, temp.Path, "third.txt", ValidationStatus.Valid, timestamp)
        };

        await store.BatchSaveResultsAsync(records, CancellationToken.None);

        var listed = await store.ListResultsAsync(session, CancellationToken.None);
        Assert.Equal(["first.txt", "second.txt", "third.txt"], listed.Select(x => x.Candidate.RelativePath).ToArray());
    }

    [Fact]
    public async Task BatchSaveResultsConcurrentAndRepeatedWrites()
    {
        using var temp = TestTempDirectory.Create();
        var db = Path.Combine(temp.Path, "state.db");
        var store = new SqliteScanStore(db);
        var options = ScanOptions.CreateDefault(temp.Path, Path.Combine(temp.Path, "target"), db);
        await store.InitializeAsync(CancellationToken.None);
        var session = await store.CreateSessionAsync(options, CancellationToken.None);

        // 1. Repeated batch writes (sequential)
        var records1 = new[] { CreateRecord(session, temp.Path, "dup.txt", ValidationStatus.Valid) };
        var records2 = new[] { CreateRecord(session, temp.Path, "dup.txt", ValidationStatus.Valid) };
        await store.BatchSaveResultsAsync(records1, CancellationToken.None);
        await store.BatchSaveResultsAsync(records2, CancellationToken.None);

        var listed = await store.ListResultsAsync(session, CancellationToken.None);
        Assert.Equal(2, listed.Count);

        // 2. High concurrency writes (parallel threads calling SaveResultAsync and BatchSaveResultsAsync)
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var fileName = $"file_{i}.txt";
            tasks.Add(Task.Run(async () =>
            {
                var record = CreateRecord(session, temp.Path, fileName, ValidationStatus.Valid);
                await store.SaveResultAsync(record, CancellationToken.None);
            }));
            tasks.Add(Task.Run(async () =>
            {
                var batch = new[] { CreateRecord(session, temp.Path, $"batch_{i}.txt", ValidationStatus.Valid) };
                await store.BatchSaveResultsAsync(batch, CancellationToken.None);
            }));
        }

        await Task.WhenAll(tasks);

        var allResults = await store.ListResultsAsync(session, CancellationToken.None);
        // We wrote 2 (dup.txt) + 10 (single save) + 10 (batch save) = 22 rows
        Assert.Equal(22, allResults.Count);
    }

    private static ScanResultRecord CreateRecord(Guid session, string root, string fileName, ValidationStatus status, DateTimeOffset? timestamp = null)
    {
        var candidate = new FileCandidate(Path.Combine(root, fileName), fileName, ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
        return new ScanResultRecord(session, candidate, status, "test", null, "dry-run", null, null, timestamp ?? DateTimeOffset.UtcNow);
    }
}
