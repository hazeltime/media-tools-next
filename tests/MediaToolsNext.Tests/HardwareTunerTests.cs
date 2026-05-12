using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class HardwareTunerTests
{
    [Fact]
    public void RecommendsBoundedConcurrency()
    {
        var profile = new HardwareTuner().Recommend(Path.GetTempPath(), Path.GetTempPath());
        Assert.InRange(profile.RecommendedConcurrency, 1, 16);
        Assert.True(profile.RecommendedCopyBufferBytes > 0);
        Assert.True(profile.RecommendedProbeSeconds > 0);
    }
}
