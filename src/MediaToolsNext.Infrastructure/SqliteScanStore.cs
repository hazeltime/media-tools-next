using MediaToolsNext.Core;
using Microsoft.Data.Sqlite;

namespace MediaToolsNext.Infrastructure;

public sealed class SqliteScanStore(string databasePath) : IScanStore
{
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                source_path TEXT NOT NULL,
                target_root TEXT NOT NULL,
                backup_root TEXT NULL,
                action_mode TEXT NOT NULL,
                started_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS results (
                session_id TEXT NOT NULL,
                full_path TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                extension TEXT NOT NULL,
                category TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                validator TEXT NOT NULL,
                detail TEXT NULL,
                action TEXT NOT NULL,
                primary_target TEXT NULL,
                backup_target TEXT NULL,
                timestamp_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid> CreateSessionAsync(ScanOptions options, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sessions VALUES ($id,$source,$target,$backup,$mode,$started)";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$source", options.SourcePath);
        command.Parameters.AddWithValue("$target", options.TargetRoot);
        command.Parameters.AddWithValue("$backup", (object?)options.BackupRoot ?? DBNull.Value);
        command.Parameters.AddWithValue("$mode", options.ActionMode.ToString());
        command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public async Task SaveResultAsync(ScanResultRecord result, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO results VALUES (
                $session,$full,$relative,$ext,$category,$size,$lastWrite,$status,$validator,$detail,
                $action,$primary,$backup,$timestamp)
            """;
        command.Parameters.AddWithValue("$session", result.SessionId.ToString());
        command.Parameters.AddWithValue("$full", result.Candidate.FullPath);
        command.Parameters.AddWithValue("$relative", result.Candidate.RelativePath);
        command.Parameters.AddWithValue("$ext", result.Candidate.Extension);
        command.Parameters.AddWithValue("$category", result.Candidate.Category.ToString());
        command.Parameters.AddWithValue("$size", result.Candidate.SizeBytes);
        command.Parameters.AddWithValue("$lastWrite", result.Candidate.LastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", result.Status.ToString());
        command.Parameters.AddWithValue("$validator", result.Validator);
        command.Parameters.AddWithValue("$detail", (object?)result.Detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$action", result.Action);
        command.Parameters.AddWithValue("$primary", (object?)result.PrimaryTargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$backup", (object?)result.BackupTargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$timestamp", result.TimestampUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanResultRecord>> ListResultsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var results = new List<ScanResultRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM results WHERE session_id = $session ORDER BY timestamp_utc";
        command.Parameters.AddWithValue("$session", sessionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadResult(reader));
        return results;
    }

    public async Task<ScanSummary> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var results = await ListResultsAsync(sessionId, cancellationToken);
        return new ScanSummary(
            sessionId,
            results.Count,
            results.Count(x => x.Status == ValidationStatus.Valid),
            results.Count(x => x.Status == ValidationStatus.Corrupt),
            results.Count(x => x.Status == ValidationStatus.Unknown),
            results.Count(x => x.Status == ValidationStatus.Error),
            results.Count(x => x.Status == ValidationStatus.Skipped));
    }

    private static ScanResultRecord ReadResult(SqliteDataReader reader)
    {
        var candidate = new FileCandidate(
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<MediaCategory>(reader.GetString(4)),
            reader.GetInt64(5),
            DateTimeOffset.Parse(reader.GetString(6)));
        return new ScanResultRecord(
            Guid.Parse(reader.GetString(0)),
            candidate,
            Enum.Parse<ValidationStatus>(reader.GetString(7)),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            DateTimeOffset.Parse(reader.GetString(13)));
    }
}
