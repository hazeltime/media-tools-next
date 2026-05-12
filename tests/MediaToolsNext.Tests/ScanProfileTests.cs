using MediaToolsNext.Core;

namespace MediaToolsNext.Tests;

public class ScanProfileTests
{
    [Fact]
    public void UnknownProfileFallsBackToFast()
    {
        Assert.Equal(ScanProfiles.FastDryRun, ScanProfiles.Get("missing"));
    }

    [Fact]
    public void PhotosProfileDisablesNonImages()
    {
        var profile = ScanProfiles.Get("photos");
        Assert.True(profile.EnableImages);
        Assert.False(profile.EnableVideo);
        Assert.False(profile.EnableAudio);
    }
}
