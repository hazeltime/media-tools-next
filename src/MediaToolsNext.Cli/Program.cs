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
var db = ValueAfter("--db") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "media-tools-next", "media-tools-next.db");
var concurrency = int.TryParse(ValueAfter("--concurrency"), out var parsed) ? parsed : Math.Max(1, Environment.ProcessorCount / 2);
var mode = args.Contains("--live") ? (backup is null ? ScanActionMode.CopySorted : ScanActionMode.CopySortedAndBackup) : ScanActionMode.DryRun;

var tools = new ExternalToolProbe();
Console.WriteLine("Tool status:");
foreach (var tool in tools.GetStatuses())
    Console.WriteLine($"- {tool.Name}: {(tool.IsAvailable ? tool.Path : "missing")}");

var options = new ScanOptions(source, target, backup, mode, true, true, true, true, concurrency, 120, db);
var pipeline = new ScannerPipeline(
    new FileDiscoverer(),
    [new ImageValidator(tools), new MediaStreamValidator(MediaCategory.Video, tools), new MediaStreamValidator(MediaCategory.Audio, tools), new DocumentValidator(tools)],
    new FileActionService(),
    new SqliteScanStore(db));

var progress = new Progress<ScanResultRecord>(r => Console.WriteLine($"{r.Status,-7} {r.Candidate.Category,-8} {r.Candidate.RelativePath}"));
var summary = await pipeline.RunAsync(options, progress, CancellationToken.None);
Console.WriteLine($"Done. total={summary.Total} valid={summary.Valid} corrupt={summary.Corrupt} unknown={summary.Unknown} errors={summary.Errors}");

string? ValueAfter(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

