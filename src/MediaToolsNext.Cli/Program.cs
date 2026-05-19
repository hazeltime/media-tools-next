using MediaToolsNext.Core;
using MediaToolsNext.Cli;
using MediaToolsNext.Infrastructure;

// -----------------------------------------------------------------------
// Graceful cancellation on Ctrl+C / SIGINT
// -----------------------------------------------------------------------
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // prevent hard kill; let pipeline wind down
    cts.Cancel();
    Console.WriteLine("Cancellation requested, stopping...");
};
var cancellationToken = cts.Token;

// -----------------------------------------------------------------------
// Argument parsing
// -----------------------------------------------------------------------
if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: MediaToolsNext.Cli <source> <target> [--backup <path>] [--live] [--move] [--flat] [--group-category] [--db <path>] [--concurrency <n>] [--probe-seconds <n>] [--profile <name>]");
    Console.WriteLine();
    Console.WriteLine("Profiles:");
    foreach (var p in ScanProfiles.All)
        Console.WriteLine($"  {p.Name,-20} {p.Description}");
    return;
}

var source = args[0];
var target = args[1];
var defaultDb = ValueAfter("--db")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "media-tools-next",
        "media-tools-next.db");

var tuning = new HardwareTuner();
var hardwareProfile = tuning.Recommend(source, target);
var build = CliScanOptionsBuilder.Build(args, hardwareProfile, defaultDb);
var options = build.Options;
var exportPath = build.ExportPath;

// -----------------------------------------------------------------------
// Tool probe
// -----------------------------------------------------------------------
var tools = new ExternalToolProbe();
Console.WriteLine("Tool status:");
foreach (var tool in tools.GetStatuses())
    Console.WriteLine($"  {tool.Name,-10} {(tool.IsAvailable ? tool.Path : "missing")}");

Console.WriteLine($"Auto tuning: concurrency={options.MaxConcurrency}, probeSeconds={options.MediaProbeSeconds}, buffer={options.CopyBufferBytes:N0} bytes, {hardwareProfile.Rationale}");

// -----------------------------------------------------------------------
// Preview mode
// -----------------------------------------------------------------------
if (args.Contains("--preview"))
{
    var preview = await new ScanPreviewService(new FileDiscoverer())
        .PreviewAsync(options, cancellationToken);
    Console.WriteLine($"Preview: {preview.TotalFiles} files, {preview.TotalBytes / 1048576d:N1} MB");
    foreach (var item in preview.FilesByCategory.Where(x => x.Value > 0))
        Console.WriteLine($"  {item.Key}: {item.Value}");
    return;
}

// -----------------------------------------------------------------------
// Run pipeline
// -----------------------------------------------------------------------
var store = new SqliteScanStore(options.DatabasePath);
var pipeline = new ScannerPipeline(
    new FileDiscoverer(),
    new ValidatorRegistry([
        new ImageValidator(tools),
        new MediaStreamValidator(MediaCategory.Video, tools),
        new MediaStreamValidator(MediaCategory.Audio, tools),
        new DocumentValidator(tools)
    ]),
    new FileActionService(),
    store,
    new ScanControl());

var metrics  = new ScanPerformanceTracker();
var progress = new Progress<ScanResultRecord>(r =>
{
    var current = metrics.Add(r);
    Console.WriteLine($"{r.Status,-7} {r.Candidate.Category,-8} {current.FilesPerSecond:N1} files/s {current.MegabytesPerSecond:N1} MB/s {r.Candidate.RelativePath}");
});

ScanSummary summary;
try
{
    summary = await pipeline.RunAsync(options, progress, cancellationToken);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Scan cancelled by user.");
    return;
}

Console.WriteLine(
    $"Done. total={summary.Total} valid={summary.Valid} corrupt={summary.Corrupt} " +
    $"unknown={summary.Unknown} errors={summary.Errors} skipped={summary.Skipped}");
if (!string.IsNullOrEmpty(summary.CompletionReason))
    Console.WriteLine($"Reason: {summary.CompletionReason}");

if (!string.IsNullOrWhiteSpace(exportPath))
{
    var records = await store.ListResultsAsync(summary.SessionId, CancellationToken.None);
    await new CsvReportExporter().ExportCsvAsync(records, exportPath, CancellationToken.None);
    Console.WriteLine($"Exported CSV: {exportPath}");
}

// -----------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------
string? ValueAfter(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
