using System.Reflection;
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
if (HasAnyArg("--version", "-v"))
{
    Console.WriteLine(GetVersion());
    return;
}

if (args.Length < 2 || HasAnyArg("--help", "-h", "-?"))
{
    Console.WriteLine("Usage: MediaToolsNext.Cli <source> <target> [options]");
    Console.WriteLine();
    Console.WriteLine("Global:");
    Console.WriteLine("  -h, -?, --help                    Show help");
    Console.WriteLine("  -v, --version                     Show version");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --preview                         Discover matches without scanning or writing");
    Console.WriteLine("  --health                          Check folders, database path, and tools without scanning");
    Console.WriteLine("  --live                            Copy or move files instead of dry-run");
    Console.WriteLine("  --move                            Move files in live mode instead of copying");
    Console.WriteLine("  --backup <path>                   Mirror sorted output to a backup target");
    Console.WriteLine("  --flat                            Write files into flat output folders with suffix collision handling");
    Console.WriteLine("  --group-category                  Group output by media category instead of validation status");
    Console.WriteLine("  --db <path>                       SQLite database path");
    Console.WriteLine("  --export <path>                   Export scan results to CSV after a run");
    Console.WriteLine("  --profile <name>                  Scan profile name");
    Console.WriteLine("  --concurrency <1-32>              Override hardware-tuned concurrency");
    Console.WriteLine("  --probe-seconds <10-600>          Override hardware-tuned media probe duration");
    Console.WriteLine("  --tool-timeout-seconds <5-600>    External tool timeout");
    Console.WriteLine("  --max-searched-files <count>      Stop after searching this many files");
    Console.WriteLine("  --max-matched-files <count>       Stop after matching this many files");
    Console.WriteLine("  --max-searched-dirs <count>       Stop after searching this many directories");
    Console.WriteLine("  --max-matched-dirs <count>        Stop after matching this many directories");
    Console.WriteLine("  --min-runtime-seconds <seconds>   Minimum runtime before search or match limits stop a run");
    Console.WriteLine("  --min-scanned-mb <mb>             Minimum scanned MB before byte limits stop a run");
    Console.WriteLine("  --max-scanned-mb <mb>             Stop after scanning this many MB");
    Console.WriteLine("  --min-matched-mb <mb>             Minimum matched MB before byte limits stop a run");
    Console.WriteLine("  --max-matched-mb <mb>             Stop after matching this many MB");
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
CliScanOptionsBuildResult build;
try
{
    build = CliScanOptionsBuilder.Build(args, hardwareProfile, defaultDb);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("Argument error: " + ex.Message);
    Console.Error.WriteLine("Run with --help for usage.");
    Environment.ExitCode = 2;
    return;
}
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
// Health mode
// -----------------------------------------------------------------------
if (args.Contains("--health"))
{
    var healthy = await RunHealthCheckAsync(options, tools, cancellationToken);
    Environment.ExitCode = healthy ? 0 : 1;
    return;
}

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

bool HasAnyArg(params string[] names) => args.Any(names.Contains);

static string GetVersion()
{
    var assembly = Assembly.GetExecutingAssembly();
    var informationalVersion = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    if (!string.IsNullOrWhiteSpace(informationalVersion))
        return informationalVersion;

    return assembly.GetName().Version?.ToString() ?? "unknown";
}

static async Task<bool> RunHealthCheckAsync(ScanOptions options, ExternalToolProbe tools, CancellationToken cancellationToken)
{
    Console.WriteLine("Health check:");
    var healthy = true;

    if (Directory.Exists(options.SourcePath))
    {
        Console.WriteLine($"  OK source exists: {options.SourcePath}");
    }
    else
    {
        Console.WriteLine($"  FAIL source folder is missing: {options.SourcePath}");
        healthy = false;
    }

    if (Directory.Exists(options.TargetRoot))
    {
        var probeFile = Path.Combine(options.TargetRoot, ".media-tools-next-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(probeFile, "test", cancellationToken);
            File.Delete(probeFile);
            Console.WriteLine($"  OK target is writable: {options.TargetRoot}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  FAIL target is not writable: {ex.Message}");
            healthy = false;
        }
    }
    else
    {
        Console.WriteLine($"  FAIL target folder is missing: {options.TargetRoot}");
        healthy = false;
    }

    var dbDir = Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath));
    if (!string.IsNullOrWhiteSpace(dbDir))
    {
        try
        {
            Directory.CreateDirectory(dbDir);
            Console.WriteLine($"  OK database folder is available: {dbDir}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  FAIL database folder is not available: {ex.Message}");
            healthy = false;
        }
    }

    foreach (var tool in tools.GetStatuses())
    {
        var status = tool.IsAvailable ? "OK" : "WARN";
        var detail = tool.IsAvailable ? tool.Path : "missing; validation may be less complete";
        Console.WriteLine($"  {status} {tool.Name}: {detail}");
    }

    Console.WriteLine(healthy ? "Health check passed." : "Health check failed.");
    return healthy;
}
