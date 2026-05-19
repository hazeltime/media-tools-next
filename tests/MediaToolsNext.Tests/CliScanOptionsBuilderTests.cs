using MediaToolsNext.Cli;
using MediaToolsNext.Core;

namespace MediaToolsNext.Tests;

public class CliScanOptionsBuilderTests
{
    [Fact]
    public void BuildUsesHardwareRecommendationsWhenCliOverridesAreAbsent()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var result = CliScanOptionsBuilder.Build(["source", "target"], hardware, "state.db");

        Assert.Equal(12, result.Options.MaxConcurrency);
        Assert.Equal(90, result.Options.MediaProbeSeconds);
        Assert.Equal(512 * 1024, result.Options.CopyBufferBytes);
        Assert.Equal(ScanProfiles.DeepImages.Name, result.Profile.Name);
    }

    [Fact]
    public void BuildHonorsCliOverridesAndWriteFlags()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var result = CliScanOptionsBuilder.Build(
            ["source", "target", "--backup", "backup", "--live", "--move", "--flat", "--group-category", "--concurrency", "99", "--probe-seconds", "5"],
            hardware,
            "state.db");

        Assert.Equal(32, result.Options.MaxConcurrency);
        Assert.Equal(10, result.Options.MediaProbeSeconds);
        Assert.Equal(ScanActionMode.CopySortedAndBackup, result.Options.ActionMode);
        Assert.Equal(FileActionOperation.Move, result.Options.ActionOperation);
        Assert.Equal(OutputPathLayout.Flat, result.Options.OutputPathLayout);
        Assert.Equal(OutputGrouping.MediaCategory, result.Options.OutputGrouping);
        Assert.Equal("backup", result.Options.BackupRoot);
    }
}
