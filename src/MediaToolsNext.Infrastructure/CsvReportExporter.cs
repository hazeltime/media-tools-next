using System.Text;
using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class CsvReportExporter : IReportExporter
{
    public async Task ExportCsvAsync(IEnumerable<ScanResultRecord> records, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("session_id,status,category,relative_path,size_bytes,validator,detail,action,primary_target,backup_target,timestamp_utc".AsMemory(), cancellationToken);
        foreach (var r in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(",", [
                Csv(r.SessionId.ToString()), Csv(r.Status.ToString()), Csv(r.Candidate.Category.ToString()),
                Csv(r.Candidate.RelativePath), r.Candidate.SizeBytes.ToString(), Csv(r.Validator), Csv(r.Detail),
                Csv(r.Action), Csv(r.PrimaryTargetPath), Csv(r.BackupTargetPath), Csv(r.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            ]);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
