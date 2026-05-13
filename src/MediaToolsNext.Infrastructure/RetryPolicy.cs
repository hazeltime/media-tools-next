using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public static class RetryPolicy
{
    /// <summary>
    /// Returns true when a validation outcome warrants an automatic retry.
    /// </summary>
    public static bool ShouldRetry(ValidationOutcome outcome) =>
        outcome.Status == ValidationStatus.Error ||
        outcome.Detail is { } detail && (detail.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                                         detail.Contains("locked", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true when a file-action exception is transient and worth retrying.
    /// Used by ScannerPipeline.ApplyWithRetryAsync so retry logic is centralised.
    /// </summary>
    public static bool ShouldRetryIOException(Exception ex) =>
        ex is IOException;
}
