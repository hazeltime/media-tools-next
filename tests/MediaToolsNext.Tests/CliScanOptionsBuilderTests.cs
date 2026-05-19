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
        Assert.Equal(15, result.Options.ExternalToolTimeoutSeconds);
        Assert.Equal(ScanProfiles.DeepImages.Name, result.Profile.Name);
    }

    [Fact]
    public void BuildHonorsCliOverridesAndWriteFlags()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var result = CliScanOptionsBuilder.Build(
            ["source", "target", "--backup", "backup", "--live", "--move", "--flat", "--group-category", "--concurrency", "99", "--probe-seconds", "5", "--tool-timeout-seconds", "42"],
            hardware,
            "state.db");

        Assert.Equal(32, result.Options.MaxConcurrency);
        Assert.Equal(10, result.Options.MediaProbeSeconds);
        Assert.Equal(42, result.Options.ExternalToolTimeoutSeconds);
        Assert.Equal(ScanActionMode.CopySortedAndBackup, result.Options.ActionMode);
        Assert.Equal(FileActionOperation.Move, result.Options.ActionOperation);
        Assert.Equal(OutputPathLayout.Flat, result.Options.OutputPathLayout);
        Assert.Equal(OutputGrouping.MediaCategory, result.Options.OutputGrouping);
        Assert.Equal("backup", result.Options.BackupRoot);
    }

    [Fact]
    public void BuildMapsSearchMatchAndByteLimits()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var result = CliScanOptionsBuilder.Build(
            [
                "source",
                "target",
                "--max-searched-files",
                "10",
                "--max-matched-files",
                "8",
                "--max-searched-dirs",
                "6",
                "--max-matched-dirs",
                "4",
                "--min-runtime-seconds",
                "3",
                "--min-scanned-mb",
                "2",
                "--max-scanned-mb",
                "5",
                "--min-matched-mb",
                "7",
                "--max-matched-mb",
                "11"
            ],
            hardware,
            "state.db");

        Assert.Equal(10, result.Options.MaxSearchedFiles);
        Assert.Equal(8, result.Options.MaxMatchedFiles);
        Assert.Equal(6, result.Options.MaxSearchedDirectories);
        Assert.Equal(4, result.Options.MaxMatchedDirectories);
        Assert.Equal(3, result.Options.MinRuntimeBeforeLimitsSeconds);
        Assert.Equal(2 * 1048576L, result.Options.MinScannedBytes);
        Assert.Equal(5 * 1048576L, result.Options.MaxScannedBytes);
        Assert.Equal(7 * 1048576L, result.Options.MinMatchedBytes);
        Assert.Equal(11 * 1048576L, result.Options.MaxMatchedBytes);
    }

    [Fact]
    public void BuildRejectsMissingOptionValue()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var ex = Assert.Throws<ArgumentException>(() =>
            CliScanOptionsBuilder.Build(["source", "target", "--db", "--live"], hardware, "state.db"));

        Assert.Contains("--db requires a value", ex.Message);
    }

    [Fact]
    public void BuildRejectsInvalidIntegerOptionValue()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var ex = Assert.Throws<ArgumentException>(() =>
            CliScanOptionsBuilder.Build(["source", "target", "--concurrency", "many"], hardware, "state.db"));

        Assert.Contains("--concurrency must be an integer", ex.Message);
    }

    [Fact]
    public void BuildRejectsNegativeIntegerOptionValue()
    {
        var hardware = new HardwareProfile(16, 0, "Fixed", "Fixed", 12, 512 * 1024, 90, "test");

        var ex = Assert.Throws<ArgumentException>(() =>
            CliScanOptionsBuilder.Build(["source", "target", "--max-searched-files", "-1"], hardware, "state.db"));

        Assert.Contains("--max-searched-files must be greater than or equal to 1", ex.Message);
    }
}
