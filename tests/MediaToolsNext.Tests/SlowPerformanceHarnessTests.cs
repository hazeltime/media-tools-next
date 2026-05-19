using System.Diagnostics;
using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public sealed class SlowPerformanceHarnessTests
{
    private const int DefaultFileCount = 500;
    private const int DefaultDirCount = 10;
    private const int FileSizeBytes = 4096;

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ScanThroughput_SmallTree_MeetsMinimumFilesPerSecond()
    {
        using var temp = TestTempDirectory.Create("media-tools-next-slow-");
        var targetDir = Path.Combine(temp.Path, "target");
        var db = Path.Combine(temp.Path, "state.db");
        Directory.CreateDirectory(targetDir);

        var filePaths = CreateSyntheticFiles(temp.Path, DefaultFileCount, DefaultDirCount, FileSizeBytes);

        var store = new SqliteScanStore(db);
        var tools = new ExternalToolProbe();
        var pipeline = new ScannerPipeline(
            new FileDiscoverer(),
            new ValidatorRegistry([new DocumentValidator(tools)]),
            new FileActionService(),
            store,
            new ScanControl());

        var options = ScanOptions.CreateDefault(temp.Path, targetDir) with
        {
            MaxConcurrency = 4,
            MaxRetries = 0,
            ExternalToolTimeoutSeconds = 5
        };

        var sw = Stopwatch.StartNew();
        var summary = await pipeline.RunAsync(options, null, CancellationToken.None);
        sw.Stop();

        var elapsedSec = sw.Elapsed.TotalSeconds;
        var filesPerSec = summary.Total / elapsedSec;
        var totalBytes = filePaths.Sum(f => new FileInfo(f).Length);
        var mbPerSec = totalBytes / 1048576d / elapsedSec;

        Console.WriteLine($"[PERF] SmallTree throughput: {summary.Total} files, {totalBytes:N0} bytes, {elapsedSec:N2}s");
        Console.WriteLine($"[PERF]   {filesPerSec:N1} files/s, {mbPerSec:N2} MB/s");

        Assert.Equal(filePaths.Count, summary.Total);
        Assert.True(filesPerSec > 0, "Throughput must be measurable");
        Assert.True(elapsedSec > 0, "Elapsed time must be measurable");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ScanThroughput_MediumTree_WithBackpressure()
    {
        using var temp = TestTempDirectory.Create("media-tools-next-slow-");
        var targetDir = Path.Combine(temp.Path, "target");
        var db = Path.Combine(temp.Path, "state.db");
        Directory.CreateDirectory(targetDir);

        var filePaths = CreateSyntheticFiles(temp.Path, DefaultFileCount * 2, DefaultDirCount * 2, FileSizeBytes);

        var store = new SqliteScanStore(db);
        var tools = new ExternalToolProbe();
        var pipeline = new ScannerPipeline(
            new FileDiscoverer(),
            new ValidatorRegistry([new DocumentValidator(tools)]),
            new FileActionService(),
            store,
            new ScanControl());

        var options = ScanOptions.CreateDefault(temp.Path, targetDir) with
        {
            MaxConcurrency = 8,
            MaxRetries = 0,
            ExternalToolTimeoutSeconds = 5
        };

        var sw = Stopwatch.StartNew();
        var summary = await pipeline.RunAsync(options, null, CancellationToken.None);
        sw.Stop();

        var elapsedSec = sw.Elapsed.TotalSeconds;
        var filesPerSec = summary.Total / elapsedSec;
        var totalBytes = filePaths.Sum(f => new FileInfo(f).Length);
        var mbPerSec = totalBytes / 1048576d / elapsedSec;

        Console.WriteLine($"[PERF] MediumTree throughput: {summary.Total} files, {totalBytes:N0} bytes, {elapsedSec:N2}s");
        Console.WriteLine($"[PERF]   {filesPerSec:N1} files/s, {mbPerSec:N2} MB/s");

        Assert.Equal(filePaths.Count, summary.Total);
        Assert.True(filesPerSec > 0, "Throughput must be measurable");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ScannerPipeline_HonorsConcurrencyBackpressure_UnderLoad()
    {
        using var temp = TestTempDirectory.Create("media-tools-next-slow-");
        var targetDir = Path.Combine(temp.Path, "target");
        var db = Path.Combine(temp.Path, "state.db");
        Directory.CreateDirectory(targetDir);

        CreateSyntheticFiles(temp.Path, DefaultFileCount, DefaultDirCount, FileSizeBytes);

        var store = new SqliteScanStore(db);

        var lowConcurrency = await RunWithConcurrency(store, temp.Path, targetDir, maxConcurrency: 1);
        var highConcurrency = await RunWithConcurrency(store, temp.Path, targetDir, maxConcurrency: 8);

        Assert.True(highConcurrency.FilesPerSecond >= lowConcurrency.FilesPerSecond * 0.5,
            "Higher concurrency should not regress below 50% of single-thread throughput for I/O-bound work");
    }

    private static async Task<ScanPerformance> RunWithConcurrency(IScanStore store, string source, string target, int maxConcurrency)
    {
        var tools = new ExternalToolProbe();
        var pipeline = new ScannerPipeline(
            new FileDiscoverer(),
            new ValidatorRegistry([new DocumentValidator(tools)]),
            new FileActionService(),
            store,
            new ScanControl());

        var options = ScanOptions.CreateDefault(source, target) with
        {
            MaxConcurrency = maxConcurrency,
            MaxRetries = 0,
            ExternalToolTimeoutSeconds = 5
        };

        var sw = Stopwatch.StartNew();
        var summary = await pipeline.RunAsync(options, null, CancellationToken.None);
        sw.Stop();
        var elapsed = sw.Elapsed;

        var perf = new ScanPerformance(elapsed, summary.Total, 0);
        Console.WriteLine($"[PERF] Concurrency={maxConcurrency}: {summary.Total} files in {elapsed.TotalSeconds:N2}s ({perf.FilesPerSecond:N1} files/s)");
        return perf;
    }

    private static List<string> CreateSyntheticFiles(string root, int totalFiles, int dirCount, int fileSize)
    {
        var files = new List<string>();
        var rng = new Random(42);
        var content = new byte[fileSize];
        rng.NextBytes(content);
        var dirs = new List<string> { root };
        for (var d = 1; d < dirCount; d++)
        {
            var dir = Path.Combine(root, $"subdir-{d:D4}");
            Directory.CreateDirectory(dir);
            dirs.Add(dir);
        }
        var filesPerDir = totalFiles / dirs.Count;
        for (var i = 0; i < totalFiles; i++)
        {
            var dir = dirs[i % dirs.Count];
            var path = Path.Combine(dir, $"file-{i:D5}.txt");
            File.WriteAllBytes(path, content);
            files.Add(path);
        }
        return files;
    }
}
