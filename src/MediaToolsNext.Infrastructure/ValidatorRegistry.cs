using MediaToolsNext.Core;

namespace MediaToolsNext.Infrastructure;

public sealed class ValidatorRegistry(IEnumerable<IMediaValidator> validators) : IValidatorRegistry
{
    private readonly IReadOnlyDictionary<MediaCategory, IMediaValidator> _validators = validators
        .GroupBy(x => x.Category)
        .ToDictionary(x => x.Key, x => x.Last());

    public IReadOnlyCollection<MediaCategory> Categories => _validators.Keys.ToArray();

    public IMediaValidator? GetValidator(MediaCategory category) =>
        _validators.TryGetValue(category, out var validator) ? validator : null;
}
