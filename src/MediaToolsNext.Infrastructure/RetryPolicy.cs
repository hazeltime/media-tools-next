using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public static class RetryPolicy
{
    public static bool ShouldRetry(ValidationOutcome outcome) =>
        outcome.Status == ValidationStatus.Error ||
        (outcome.Detail?.Contains("timeout", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (outcome.Detail?.Contains("locked", StringComparison.OrdinalIgnoreCase) ?? false);
}
