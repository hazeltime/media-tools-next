using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class RetryPolicyTests
{
    [Fact]
    public void RetriesErrorsAndTimeoutsOnly()
    {
        var candidate = new FileCandidate("a", "a", ".txt", MediaCategory.Document, 1, DateTimeOffset.UtcNow);
        Assert.True(RetryPolicy.ShouldRetry(new ValidationOutcome(candidate, ValidationStatus.Error, "test", "access", TimeSpan.Zero)));
        Assert.True(RetryPolicy.ShouldRetry(new ValidationOutcome(candidate, ValidationStatus.Corrupt, "test", "timeout", TimeSpan.Zero)));
        Assert.False(RetryPolicy.ShouldRetry(new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero)));
    }
}
