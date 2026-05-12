namespace MediaToolsNext.Core;

public interface IFileDiscoverer
{
    IAsyncEnumerable<FileCandidate> DiscoverAsync(ScanOptions options, CancellationToken cancellationToken);
}

public interface IMediaValidator
{
    MediaCategory Category { get; }
    Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken);
}

public interface IValidatorRegistry
{
    IReadOnlyCollection<MediaCategory> Categories { get; }
    IMediaValidator? GetValidator(MediaCategory category);
}

public interface IFileActionService
{
    Task<FileActionOutcome> ApplyAsync(ValidationOutcome outcome, ScanOptions options, CancellationToken cancellationToken);
}

public interface IScanStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<Guid> CreateSessionAsync(ScanOptions options, CancellationToken cancellationToken);
    Task SaveResultAsync(ScanResultRecord result, CancellationToken cancellationToken);
    Task<ScanResultRecord?> FindReusableResultAsync(FileCandidate candidate, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanResultRecord>> ListResultsAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanSessionRecord>> ListSessionsAsync(int take, CancellationToken cancellationToken);
    Task<ScanSummary> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken);
}

public interface IExternalToolProbe
{
    IReadOnlyList<ToolStatus> GetStatuses();
    string? FindExecutable(string commandName);
}

public interface IScannerPipeline
{
    Task<ScanSummary> RunAsync(ScanOptions options, IProgress<ScanResultRecord>? progress, CancellationToken cancellationToken);
}

public interface IHardwareTuner
{
    HardwareProfile Recommend(string sourcePath, string targetPath);
}

public interface IScanPreviewService
{
    Task<ScanPreview> PreviewAsync(ScanOptions options, CancellationToken cancellationToken, IProgress<ScanPreview>? progress = null);
}

public interface IReportExporter
{
    Task ExportCsvAsync(IEnumerable<ScanResultRecord> records, string path, CancellationToken cancellationToken);
}
