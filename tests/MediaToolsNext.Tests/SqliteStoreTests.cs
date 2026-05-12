using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class SqliteStoreTests
{
    [Fact]
    public async Task StoresAndSummarizesResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "state.db");
            var store = new SqliteScanStore(db);
            var options = ScanOptions.CreateDefault(root, Path.Combine(root, "target"), db);
            await store.InitializeAsync(CancellationToken.None);
            var session = await store.CreateSessionAsync(options, CancellationToken.None);
            var candidate = new FileCandidate(Path.Combine(root, "a.txt"), "a.txt", ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
            await store.SaveResultAsync(new ScanResultRecord(session, candidate, ValidationStatus.Valid, "test", null, "dry-run", null, null, DateTimeOffset.UtcNow), CancellationToken.None);
            var summary = await store.GetSummaryAsync(session, CancellationToken.None);
            Assert.Equal(1, summary.Total);
            Assert.Equal(1, summary.Valid);
        }
        finally { Directory.Delete(root, true); }
    }
}

