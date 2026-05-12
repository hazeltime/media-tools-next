using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: MediaToolsNext.Cli <source> <target> [--backup <path>] [--live] [--db <path>] [--concurrency <n>]");
    return;
}

var source = args[0];
var target = args[1];
var backup = ValueAfter("--backup");
var profile = ScanProfiles.Get(ValueAfter("--profile"));
var exportPath = ValueAfter("--export");
var db = ValueAfter("--db") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "media-tools-next", "media-tools-next.db");
var tuning = new HardwareTuner().Recommend(source, target);
var concurrency = int.TryParse(ValueAfter("--concurrency"), out var parsed) ? parsed : tuning.RecommendedConcurrency;
var mode = args.Contains("--live") ? (backup is null ? ScanActionMode.CopySorted : ScanActionMode.CopySortedAndBackup) : profile.DefaultActionMode;

var tools = new ExternalToolProbe();
Console.WriteLine("Tool status:");
foreach (var tool in tools.GetStatuses())
    Console.WriteLine($"- {tool.Name}: {(tool.IsAvailable ? tool.Path : "missing")}");

Console.WriteLine($"Auto tuning: concurrency={concurrency}, probeSeconds={tuning.RecommendedProbeSeconds}, buffer={tuning.RecommendedCopyBufferBytes}, {tuning.Rationale}");
var options = new ScanOptions(source, target, backup, mode, profile.EnableImages, profile.EnableVideo, profile.EnableAudio, profile.EnableDocuments, concurrency, profile.MediaProbeSeconds, db, profile.ValidationDepth);
if (args.Contains("--preview"))
{
    var preview = await new ScanPreviewService(new FileDiscoverer()).PreviewAsync(options, CancellationToken.None);
    Console.WriteLine($"Preview: {preview.TotalFiles} files, {preview.TotalBytes / 1048576d:N1} MB");
    foreach (var item in preview.FilesByCategory.Where(x => x.Value > 0))
        Console.WriteLine($"- {item.Key}: {item.Value}");
    return;
}
var store = new SqliteScanStore(db);
var pipeline = new ScannerPipeline(
    new FileDiscoverer(),
    new ValidatorRegistry([new ImageValidator(tools), new MediaStreamValidator(MediaCategory.Video, tools), new MediaStreamValidator(MediaCategory.Audio, tools), new DocumentValidator(tools)]),
    new FileActionService(),
    store);

var metrics = new ScanPerformanceTracker();
var progress = new Progress<ScanResultRecord>(r =>
{
    var current = metrics.Add(r);
    Console.WriteLine($"{r.Status,-7} {r.Candidate.Category,-8} {current.FilesPerSecond:N1} files/s {current.MegabytesPerSecond:N1} MB/s {r.Candidate.RelativePath}");
});
var summary = await pipeline.RunAsync(options, progress, CancellationToken.None);
Console.WriteLine($"Done. total={summary.Total} valid={summary.Valid} corrupt={summary.Corrupt} unknown={summary.Unknown} errors={summary.Errors}");
if (!string.IsNullOrWhiteSpace(exportPath))
{
    var records = await store.ListResultsAsync(summary.SessionId, CancellationToken.None);
    await new CsvReportExporter().ExportCsvAsync(records, exportPath, CancellationToken.None);
    Console.WriteLine($"Exported CSV: {exportPath}");
}

string? ValueAfter(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
