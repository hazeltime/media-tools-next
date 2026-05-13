using MediaToolsNext.Core;
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
    Console.WriteLine("Usage: MediaToolsNext.Cli <source> <target> [--backup <path>] [--live] [--move] [--flat] [--group-category] [--db <path>] [--concurrency <n>] [--profile <name>]");
    Console.WriteLine();
    Console.WriteLine("Profiles:");
    foreach (var p in ScanProfiles.All)
        Console.WriteLine($"  {p.Name,-20} {p.Description}");
    return;
}

var source     = args[0];
var target     = args[1];
var backup     = ValueAfter("--backup");
var profile    = ScanProfiles.Get(ValueAfter("--profile") ?? ScanProfiles.DeepImages.Name);
var exportPath = ValueAfter("--export");
var db         = ValueAfter("--db")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "media-tools-next",
        "media-tools-next.db");

var tuning = new HardwareTuner();
        var hardwareProfile = new HardwareProfile(Environment.ProcessorCount, 0, "", "", Math.Max(1, (Environment.ProcessorCount + 1) / 2), 1048576, 120, "");
        try { hardwareProfile = tuning.Recommend(source, target); }
        catch { }
        var recommendedConcurrency = hardwareProfile.RecommendedConcurrency;
        var recommendedProbeSeconds = hardwareProfile.RecommendedProbeSeconds;
var concurrency = int.TryParse(ValueAfter("--concurrency"), out var parsed)
    ? Math.Clamp(parsed, 1, 32)
    : 8;
var mode        = args.Contains("--live")
    ? (backup is null ? ScanActionMode.CopySorted : ScanActionMode.CopySortedAndBackup)
    : profile.DefaultActionMode;

// -----------------------------------------------------------------------
// Tool probe
// -----------------------------------------------------------------------
var tools = new ExternalToolProbe();
Console.WriteLine("Tool status:");
foreach (var tool in tools.GetStatuses())
    Console.WriteLine($"  {tool.Name,-10} {(tool.IsAvailable ? tool.Path : "missing")}");

Console.WriteLine($"Auto tuning: concurrency={concurrency}, probeSeconds={recommendedProbeSeconds}, buffer={hardwareProfile.RecommendedCopyBufferBytes:N0} bytes, {hardwareProfile.Rationale}");
var operation = args.Contains("--move") ? FileActionOperation.Move : FileActionOperation.Copy;
var grouping = args.Contains("--group-category") ? OutputGrouping.MediaCategory : OutputGrouping.Status;
var layout = args.Contains("--flat") ? OutputPathLayout.Flat : OutputPathLayout.PreserveRelativePath;

// -----------------------------------------------------------------------
// Build options
// -----------------------------------------------------------------------
var options = new ScanOptions(
    source,
    target,
    backup,
    mode,
    profile.EnableImages,
    profile.EnableVideo,
    profile.EnableAudio,
    profile.EnableDocuments,
    concurrency,
    profile.MediaProbeSeconds,
    db,
    profile.ValidationDepth,
    MaxSearchedFiles:        IntAfter("--max-searched-files"),
    MaxMatchedFiles:         IntAfter("--max-matched-files"),
    MaxSearchedDirectories:  IntAfter("--max-searched-dirs"),
    MaxMatchedDirectories:   IntAfter("--max-matched-dirs"),
    MinRuntimeBeforeLimitsSeconds: IntAfter("--min-runtime-seconds") ?? 0,
    MinScannedBytes:         MbAfter("--min-scanned-mb"),
    MaxScannedBytes:         MbAfter("--max-scanned-mb"),
    MinMatchedBytes:         MbAfter("--min-matched-mb"),
    MaxMatchedBytes:         MbAfter("--max-matched-mb"),
    ExternalToolTimeoutSeconds: IntAfter("--tool-timeout-seconds") ?? 15,
    ActionOperation: operation,
    OutputGrouping: grouping,
    OutputPathLayout: layout);

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
var store = new SqliteScanStore(db);
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

int? IntAfter(string name) => int.TryParse(ValueAfter(name), out var value) && value > 0 ? value : null;
long? MbAfter(string name) => IntAfter(name) is int value ? value * 1048576L : null;
