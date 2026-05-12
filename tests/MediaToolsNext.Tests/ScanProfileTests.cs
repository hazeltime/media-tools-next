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
    public void ImageProfilesDisableNonImages()
    {
        var profile = ScanProfiles.Get("standard-images");
        Assert.True(profile.EnableImages);
        Assert.False(profile.EnableVideo);
        Assert.False(profile.EnableAudio);
        Assert.False(profile.EnableDocuments);
    }

    [Fact]
    public void DeepImageProfileUsesDeepValidation()
    {
        Assert.Equal(ValidationDepth.Deep, ScanProfiles.DeepImages.ValidationDepth);
    }
}
