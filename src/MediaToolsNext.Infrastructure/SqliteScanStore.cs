using MediaToolsNext.Core;
using Microsoft.Data.Sqlite;

namespace MediaToolsNext.Infrastructure;

public sealed class SqliteScanStore(string databasePath) : IScanStore
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    private string ConnectionString => _connectionString;

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Keep wait time bounded on every connection so short write contention
        // does not surface as a transient lock failure.
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using (var pragmaCommand = connection.CreateCommand())
        {
            // WAL is persistent for the database file, so set it once during
            // initialization instead of on every connection open.
            pragmaCommand.CommandText = "PRAGMA journal_mode=WAL;";
            await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await ConfigureConnectionAsync(connection, cancellationToken);
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
            CREATE INDEX IF NOT EXISTS idx_results_session_id ON results (session_id);
            CREATE INDEX IF NOT EXISTS idx_results_full_path ON results (full_path, size_bytes, last_write_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid> CreateSessionAsync(ScanOptions options, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sessions VALUES ($id,$source,$target,$backup,$mode,$started)";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$source", options.SourcePath);
        command.Parameters.AddWithValue("$target", options.TargetRoot);
        command.Parameters.AddWithValue("$backup", (object?)options.BackupRoot ?? DBNull.Value);
        command.Parameters.AddWithValue("$mode", options.ActionMode.ToString());
        command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString(TimestampFormat));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public async Task SaveResultAsync(ScanResultRecord result, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await InsertResultAsync(connection, result, cancellationToken);
    }

    /// <summary>
    /// Inserts a batch of results in a single transaction. Prefer this over
    /// repeated SaveResultAsync calls when writing many rows at once.
    /// </summary>
    public async Task BatchSaveResultsAsync(IEnumerable<ScanResultRecord> results, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var result in results)
            await InsertResultAsync(connection, result, cancellationToken, tx as SqliteTransaction);
        await tx.CommitAsync(cancellationToken);
    }

    private static async Task InsertResultAsync(
        SqliteConnection connection,
        ScanResultRecord result,
        CancellationToken cancellationToken,
        SqliteTransaction? tx = null)
    {
        var command = connection.CreateCommand();
        if (tx is not null) command.Transaction = tx;
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
        command.Parameters.AddWithValue("$lastWrite", result.Candidate.LastWriteTimeUtc.ToString(TimestampFormat));
        command.Parameters.AddWithValue("$status", result.Status.ToString());
        command.Parameters.AddWithValue("$validator", result.Validator);
        command.Parameters.AddWithValue("$detail", (object?)result.Detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$action", result.Action);
        command.Parameters.AddWithValue("$primary", (object?)result.PrimaryTargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$backup", (object?)result.BackupTargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$timestamp", result.TimestampUtc.ToString(TimestampFormat));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ScanResultRecord?> FindReusableResultAsync(FileCandidate candidate, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM results
            WHERE full_path = $full AND size_bytes = $size AND last_write_utc = $lastWrite
              AND status IN ('Valid','Corrupt','Unknown')
            ORDER BY timestamp_utc DESC
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$full", candidate.FullPath);
        command.Parameters.AddWithValue("$size", candidate.SizeBytes);
        command.Parameters.AddWithValue("$lastWrite", candidate.LastWriteTimeUtc.ToString(TimestampFormat));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadResult(reader) : null;
    }

    public async Task<IReadOnlyList<ScanResultRecord>> ListResultsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var results = new List<ScanResultRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM results WHERE session_id = $session ORDER BY timestamp_utc";
        command.Parameters.AddWithValue("$session", sessionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadResult(reader));
        return results;
    }

    public async Task<IReadOnlyList<ScanSessionRecord>> ListSessionsAsync(int take, CancellationToken cancellationToken)
    {
        var sessions = new List<ScanSessionRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_path, target_root, backup_root, action_mode, started_utc
            FROM sessions
            ORDER BY started_utc DESC
            LIMIT $take
            """;
        command.Parameters.AddWithValue("$take", Math.Max(1, take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new ScanSessionRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Enum.Parse<ScanActionMode>(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5))));
        }
        return sessions;
    }

    public async Task<ScanSummary> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        // Aggregate in SQL — never load all rows into memory for a summary.
        command.CommandText = """
            SELECT status, COUNT(*) as cnt
            FROM results
            WHERE session_id = $session
            GROUP BY status
            """;
        command.Parameters.AddWithValue("$session", sessionId.ToString());

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            counts[reader.GetString(0)] = reader.GetInt32(1);

        int Get(ValidationStatus s) => counts.TryGetValue(s.ToString(), out var n) ? n : 0;
        var total = counts.Values.Sum();
        return new ScanSummary(
            sessionId,
            total,
            Get(ValidationStatus.Valid),
            Get(ValidationStatus.Corrupt),
            Get(ValidationStatus.Unknown),
            Get(ValidationStatus.Error),
            Get(ValidationStatus.Skipped));
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
