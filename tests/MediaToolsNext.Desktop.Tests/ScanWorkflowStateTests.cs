using MediaToolsNext.Core;
using MediaToolsNext.Desktop;

namespace MediaToolsNext.Desktop.Tests;

public class ScanWorkflowStateTests
{
    [Fact]
    public void DefaultsDoNotContainDeveloperDrivePaths()
    {
        var state = new ScanWorkflowState();

        Assert.Equal(string.Empty, state.SourcePath);
        Assert.Equal(string.Empty, state.TargetRoot);
        Assert.Null(state.BackupRoot);
        Assert.Contains("Source folder must exist.", state.PreflightErrors());
    }

    [Fact]
    public void BuildOptionsCarriesGlobalFiltersAndEmptyBackupAsNull()
    {
        var source = NewTempDir();
        try
        {
            var state = new ScanWorkflowState
            {
                SourcePath = source,
                TargetRoot = Path.Combine(source, "target"),
                BackupRoot = "   ",
                IncludePatternsText = "IMG_* *.jpg",
                ExcludePatternsText = "thumb_*;*_small.*",
                MinFileKb = 2,
                MaxFileMb = 3
            };

            var options = state.BuildOptions();

            Assert.Null(options.BackupRoot);
            Assert.Equal(["IMG_*", "*.jpg"], options.IncludeFileNamePatterns);
            Assert.Equal(["thumb_*", "*_small.*"], options.ExcludeFileNamePatterns);
            Assert.Equal(2 * 1024, options.MinCandidateBytes);
            Assert.Equal(3 * 1024 * 1024, options.MaxCandidateBytes);
        }
        finally { Directory.Delete(source, true); }
    }

    [Fact]
    public void ApplyProfileUpdatesFamiliesProbeAndDepth()
    {
        var state = new ScanWorkflowState();

        state.ApplyProfile(ScanProfiles.AllMedia.Name);

        Assert.Equal(ScanProfiles.AllMedia.Name, state.ProfileName);
        Assert.True(state.EnableImages);
        Assert.True(state.EnableVideo);
        Assert.True(state.EnableAudio);
        Assert.True(state.EnableDocuments);
        Assert.Equal(ScanProfiles.AllMedia.MediaProbeSeconds, state.ProbeSeconds);
        Assert.Equal(ScanProfiles.AllMedia.ValidationDepth, state.ValidationDepth);
    }

    [Fact]
    public void ExtensionHelpersNormalizeAndToggleExtensions()
    {
        var parsed = ScanWorkflowState.ParseExtensions("jpg .PNG *.webp");
        var state = new ScanWorkflowState();

        state.ToggleExtension("*.jpg", enabled: false);
        state.ToggleExtension("custom", enabled: true);

        Assert.Equal([".jpg", ".png", ".webp"], parsed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Assert.False(state.IsExtensionEnabled(".jpg"));
        Assert.True(state.IsExtensionEnabled(".custom"));
    }

    [Fact]
    public void PreflightRequiresTargetAndBackupOnlyForWriteModes()
    {
        var source = NewTempDir();
        try
        {
            var state = new ScanWorkflowState
            {
                SourcePath = source,
                Mode = ScanActionMode.CopySortedAndBackup,
                ConfirmedLiveMode = true
            };

            var errors = state.PreflightErrors().ToArray();

            Assert.Contains("Target folder is required for write modes.", errors);
            Assert.Contains("Backup folder is required for backup mode.", errors);
        }
        finally { Directory.Delete(source, true); }
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "media-tools-next-desktop-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }
}
