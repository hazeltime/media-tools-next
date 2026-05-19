using MediaToolsNext.Core;

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
    public void ToggleCopyStatusMutatesSetAndNotifiesSubscribers()
    {
        var state = new ScanWorkflowState();
        var changed = 0;
        state.Changed += () => changed++;

        state.ToggleCopyStatus(ValidationStatus.Valid, enabled: false);
        state.ToggleCopyStatus(ValidationStatus.Valid, enabled: true);

        Assert.Contains(ValidationStatus.Valid, state.CopyStatuses);
        Assert.Equal(2, changed);
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

    [Fact]
    public void PreflightReportsInvalidLimitsRegexAndMissingEnabledExtensions()
    {
        var source = NewTempDir();
        try
        {
            var state = new ScanWorkflowState
            {
                SourcePath = source,
                CustomImageRegex = "[",
                Concurrency = 0,
                ToolTimeoutSeconds = 4,
                ProbeSeconds = 9,
                MaxRuntimeSeconds = -1,
                MaxMatchedFiles = -1,
                MaxMatchedMb = -1,
                MinFileKb = 2049,
                MaxFileMb = 2
            };
            state.SelectNoExtensions(MediaCategory.Image);

            var errors = state.PreflightErrors().ToArray();

            Assert.Contains("Custom image regex is invalid.", errors);
            Assert.Contains("Concurrency must be between 1 and 32.", errors);
            Assert.Contains("Tool timeout must be between 5 and 600 seconds.", errors);
            Assert.Contains("Media probe seconds must be between 10 and 600.", errors);
            Assert.Contains("Max scan time cannot be negative.", errors);
            Assert.Contains("Max matched files cannot be negative.", errors);
            Assert.Contains("Max total matched MB cannot be negative.", errors);
            Assert.Contains("Minimum file size cannot exceed maximum file size.", errors);
            Assert.Contains("Select at least one default extension for an enabled file family, or add a custom image type.", errors);
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
