using MediaToolsNext.Core;
using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ValidatorRegistryTests
{
    [Fact]
    public void RegistryReturnsValidatorByCategory()
    {
        var validator = new TestValidator();
        var registry = new ValidatorRegistry([validator]);
        Assert.Same(validator, registry.GetValidator(MediaCategory.Document));
        Assert.Null(registry.GetValidator(MediaCategory.Image));
    }

    private sealed class TestValidator : IMediaValidator
    {
        public MediaCategory Category => MediaCategory.Document;
        public Task<ValidationOutcome> ValidateAsync(FileCandidate candidate, ScanOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(new ValidationOutcome(candidate, ValidationStatus.Valid, "test", null, TimeSpan.Zero));
    }
}
