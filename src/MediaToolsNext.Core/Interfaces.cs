namespace MediaToolsNext.Core;

/// <summary>
/// Discovers media files in a source directory to be processed by the scanner.
/// </summary>
public interface IFileDiscoverer
{
    /// <summary>
    /// Asynchronously enumerates file candidates matching the criteria in the provided options.
    /// </summary>
    IAsyncEnumerable<FileCandidate> DiscoverAsync(
        ScanOptions options,
        CancellationToken cancellationToken,
        IProgress<ScanDiscoveryEvent>? discoveryProgress = null);
}

/// <summary>
/// Validates a specific category of media files (e.g. Image, Video).
/// </summary>
public interface IMediaValidator
{
    /// <summary>
    /// Gets the media category handled by this validator.
    /// </summary>
    MediaCategory Category { get; }
    
    /// <summary>
    /// Performs validation checks on the given file candidate.
    /// </summary>
    Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Maintains a registry of available media validators.
/// </summary>
public interface IValidatorRegistry
{
    /// <summary>
    /// Gets the categories supported by the registered validators.
    /// </summary>
    IReadOnlyCollection<MediaCategory> Categories { get; }
    
    /// <summary>
    /// Retrieves the validator associated with a specific media category.
    /// </summary>
    IMediaValidator? GetValidator(MediaCategory category);
}

/// <summary>
/// Executes file operations (copy, move) based on validation outcomes.
/// </summary>
public interface IFileActionService
{
    /// <summary>
    /// Applies the configured file action (e.g., copying to a sorted folder) based on the validation outcome.
    /// </summary>
    Task<FileActionOutcome> ApplyAsync(ValidationOutcome outcome, ScanOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Manages the persistence of scan sessions and results to a backing store.
/// </summary>
public interface IScanStore
{
    /// <summary>
    /// Initializes the data store (e.g., creating database schemas).
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Creates a new scan session record and returns its unique identifier.
    /// </summary>
    Task<Guid> CreateSessionAsync(ScanOptions options, CancellationToken cancellationToken);
    
    /// <summary>
    /// Saves a single scan result.
    /// </summary>
    Task SaveResultAsync(ScanResultRecord result, CancellationToken cancellationToken);
    
    /// <summary>
    /// Saves a batch of scan results efficiently.
    /// </summary>
    Task BatchSaveResultsAsync(IEnumerable<ScanResultRecord> results, CancellationToken cancellationToken);
    
    /// <summary>
    /// Retrieves all results associated with a specific scan session.
    /// </summary>
    Task<IReadOnlyList<ScanResultRecord>> ListResultsAsync(Guid sessionId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Retrieves a list of recent scan sessions.
    /// </summary>
    Task<IReadOnlyList<ScanSessionRecord>> ListSessionsAsync(int take, CancellationToken cancellationToken);
    
    /// <summary>
    /// Computes and returns a summary of the outcomes for a given scan session.
    /// </summary>
    Task<ScanSummary> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken);
}

/// <summary>
/// Probes the system for the availability of external processing tools (e.g., ffmpeg, imagemagick).
/// </summary>
public interface IExternalToolProbe
{
    /// <summary>
    /// Gets the current status of all known external tools.
    /// </summary>
    IReadOnlyList<ToolStatus> GetStatuses();
    
    /// <summary>
    /// Attempts to find the executable path for a specific command name.
    /// </summary>
    string? FindExecutable(string commandName);
    
    /// <summary>
    /// Refreshes the cached status of the tools.
    /// </summary>
    void Refresh() { }
}

/// <summary>
/// Orchestrates the end-to-end media scanning pipeline.
/// </summary>
public interface IScannerPipeline
{
    /// <summary>
    /// Runs the scanning pipeline, coordinating discovery, validation, and file actions.
    /// </summary>
    Task<ScanSummary> RunAsync(
        ScanOptions options,
        IProgress<ScanResultRecord>? progress,
        CancellationToken cancellationToken,
        IProgress<ScanDiscoveryEvent>? discoveryProgress = null);
}

/// <summary>
/// Recommends concurrency and timeout settings based on hardware and drive performance.
/// </summary>
public interface IHardwareTuner
{
    /// <summary>
    /// Recommends a hardware profile suitable for the given source and target paths.
    /// </summary>
    HardwareProfile Recommend(string sourcePath, string targetPath);
}

/// <summary>
/// Provides preview capabilities for a scan without executing file actions.
/// </summary>
public interface IScanPreviewService
{
    /// <summary>
    /// Generates a preview of the scan operation, detailing what files would be processed.
    /// </summary>
    Task<ScanPreview> PreviewAsync(ScanOptions options, CancellationToken cancellationToken, IProgress<ScanPreview>? progress = null);
}

/// <summary>
/// Exports scan results to external formats.
/// </summary>
public interface IReportExporter
{
    /// <summary>
    /// Exports the provided scan records to a CSV file at the specified path.
    /// </summary>
    Task ExportCsvAsync(IEnumerable<ScanResultRecord> records, string path, CancellationToken cancellationToken);
}
