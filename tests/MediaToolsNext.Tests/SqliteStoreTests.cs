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
            CreateRecord(session, temp.Path, "error.txt", ValidationStatus.Error),
            CreateRecord(session, temp.Path, "unknown.txt", ValidationStatus.Unknown)
        };

        await store.BatchSaveResultsAsync(records, CancellationToken.None);

        var listed = await store.ListResultsAsync(session, CancellationToken.None);
        var summary = await store.GetSummaryAsync(session, CancellationToken.None);
        Assert.Equal(3, listed.Count);
        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Valid);
        Assert.Equal(1, summary.Errors);
        Assert.Equal(1, summary.Unknown);
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

    private static ScanResultRecord CreateRecord(Guid session, string root, string fileName, ValidationStatus status)
    {
        var candidate = new FileCandidate(Path.Combine(root, fileName), fileName, ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
        return new ScanResultRecord(session, candidate, status, "test", null, "dry-run", null, null, DateTimeOffset.UtcNow);
    }
}
