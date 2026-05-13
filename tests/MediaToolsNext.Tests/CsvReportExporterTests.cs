using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class CsvReportExporterTests
{
    [Fact]
    public async Task WritesCsvWithEscapedValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "report.csv");
            var candidate = new FileCandidate("a", "a,b.txt", ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
            var record = new ScanResultRecord(Guid.NewGuid(), candidate, ValidationStatus.Valid, "test", "x,y", "dry-run", null, null, DateTimeOffset.UtcNow);
            await new CsvReportExporter().ExportCsvAsync([record], path, CancellationToken.None);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("\"a,b.txt\"", text);
            Assert.Contains("\"x,y\"", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExportCsvAsyncHonorsCancellationBeforeWritingRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-tools-next-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "report.csv");
            var candidate = new FileCandidate("a", "plain.txt", ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
            var record = new ScanResultRecord(Guid.NewGuid(), candidate, ValidationStatus.Valid, "test", "detail", "dry-run", null, null, DateTimeOffset.UtcNow);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new CsvReportExporter().ExportCsvAsync([record], path, cts.Token));
        }
        finally { Directory.Delete(root, true); }
    }
}
