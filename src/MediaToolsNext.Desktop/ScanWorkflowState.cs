using MediaToolsNext.Core;

namespace MediaToolsNext.Desktop;

public sealed class ScanWorkflowState
{
    public string SourcePath { get; set; } = @"E:\q";
    public string TargetRoot { get; set; } = @"E:\_output_";
    public string? BackupRoot { get; set; } = @"G:\_backup_";
    public ScanActionMode Mode { get; set; } = ScanActionMode.DryRun;
    public string ProfileName { get; set; } = ScanProfiles.FastDryRun.Name;
    public ValidationDepth ValidationDepth { get; set; } = ValidationDepth.Fast;
    public bool EnableImages { get; set; } = true;
    public bool EnableVideo { get; set; }
    public bool EnableAudio { get; set; }
    public bool EnableDocuments { get; set; }
    public int Concurrency { get; set; } = 4;
    public int ProbeSeconds { get; set; } = 10;
    public int ToolTimeoutSeconds { get; set; } = 30;
    public int MaxRuntimeSeconds { get; set; } = 30;
    public int MaxMatchedFiles { get; set; } = 10000;
    public int MaxMatchedMb { get; set; } = 10240;
    public int MinFileKb { get; set; }
    public int MaxFileMb { get; set; }
    public bool ForceRescan { get; set; }
    public bool ConfirmedLiveMode { get; set; }
    public string CustomImageExtensionsText { get; set; } = string.Empty;
    public string CustomImageRegex { get; set; } = string.Empty;
    public string IncludePatternsText { get; set; } = string.Empty;
    public string ExcludePatternsText { get; set; } = string.Empty;
    public string ReportFormat { get; set; } = "csv";
    public string ReportSort { get; set; } = "status-path";
    public bool GroupReportByStatus { get; set; } = true;
    public HashSet<ValidationStatus> CopyStatuses { get; } = [ValidationStatus.Valid, ValidationStatus.Corrupt, ValidationStatus.Unknown, ValidationStatus.Error];
    public List<ScanResultRecord> Results { get; } = [];
    public List<ScanSessionRecord> Sessions { get; } = [];
    public ScanSummary? Summary { get; set; }
    public ScanPerformance Performance { get; set; } = new(TimeSpan.Zero, 0, 0);
    public ScanPreview? Preview { get; set; }
    public string LastCompletionReason { get; set; } = "Not run yet";
    public string? Message { get; set; }
    public string LastRunType { get; private set; } = "None";
    public DateTimeOffset? LastRunStartedLocal { get; private set; }
    public DateTimeOffset? LastRunEndedLocal { get; private set; }
    public string LastRunSettings { get; private set; } = "No run yet.";
    public bool IsPreviewing { get; private set; }
    public bool IsScanning { get; private set; }
    public bool IsBusy => IsPreviewing || IsScanning;
    public CancellationToken ScanCancellationToken => scanCts?.Token ?? CancellationToken.None;
    public event Action? Changed;

    private readonly object runGate = new();
    private CancellationTokenSource? scanCts;

    public bool IsImageOnly => EnableImages && !EnableVideo && !EnableAudio && !EnableDocuments;
    public bool AnyFamilyEnabled => EnableImages || EnableVideo || EnableAudio || EnableDocuments;
    public IReadOnlySet<string> CustomImageExtensions => ParseExtensions(CustomImageExtensionsText);
    public IReadOnlyList<string> IncludePatterns => ParseList(IncludePatternsText);
    public IReadOnlyList<string> ExcludePatterns => ParseList(ExcludePatternsText);

    public bool TryStartPreview()
    {
        lock (runGate)
        {
            if (IsBusy) return false;
            IsPreviewing = true;
            LastRunType = "Preview";
            LastRunStartedLocal = DateTimeOffset.Now;
            LastRunEndedLocal = null;
            LastRunSettings = BuildRunSettingsSummary();
            Preview = null;
            Message = "Preview is discovering matching files...";
        }

        NotifyChanged();
        return true;
    }

    public void FinishPreview(string message)
    {
        lock (runGate)
        {
            IsPreviewing = false;
            LastRunEndedLocal = DateTimeOffset.Now;
            Message = message;
        }

        NotifyChanged();
    }

    public bool TryStartScan()
    {
        lock (runGate)
        {
            if (IsBusy) return false;
            IsScanning = true;
            LastRunType = "Scan";
            LastRunStartedLocal = DateTimeOffset.Now;
            LastRunEndedLocal = null;
            LastRunSettings = BuildRunSettingsSummary();
            scanCts?.Dispose();
            scanCts = new CancellationTokenSource();
            Results.Clear();
            Summary = null;
            Performance = new(TimeSpan.Zero, 0, 0);
            Message = "Scan is running...";
        }

        NotifyChanged();
        return true;
    }

    public void FinishScan(string message)
    {
        lock (runGate)
        {
            IsScanning = false;
            LastRunEndedLocal = DateTimeOffset.Now;
            scanCts?.Dispose();
            scanCts = null;
            Message = message;
        }

        NotifyChanged();
    }

    public void CancelScan()
    {
        scanCts?.Cancel();
        Message = "Cancelling scan...";
        NotifyChanged();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public string BuildRunSettingsSummary()
    {
        var families = string.Join(", ", new[]
        {
            EnableImages ? "images" : null,
            EnableVideo ? "video" : null,
            EnableAudio ? "audio" : null,
            EnableDocuments ? "documents" : null
        }.Where(x => x is not null));
        var limits = $"limits: {FormatLimit(MaxRuntimeSeconds, "s")}, {FormatLimit(MaxMatchedFiles, "files")}, {FormatLimit(MaxMatchedMb, "MB total")}";
        var sizeFilter = MinFileKb > 0 || MaxFileMb > 0
            ? $", per-file size: min {FormatLimit(MinFileKb, "KB")}, max {FormatLimit(MaxFileMb, "MB")}"
            : string.Empty;
        var patterns = IncludePatterns.Count > 0 || ExcludePatterns.Count > 0
            ? $", patterns: include {IncludePatterns.Count}, exclude {ExcludePatterns.Count}"
            : string.Empty;

        return $"{Mode}, {DepthLabel(ValidationDepth)}, {families}; {limits}{sizeFilter}{patterns}; cache {(ForceRescan ? "off" : "on")}.";
    }

    public void ApplyProfile(string name)
    {
        ProfileName = name;
        var profile = ScanProfiles.Get(name);
        EnableImages = profile.EnableImages;
        EnableVideo = profile.EnableVideo;
        EnableAudio = profile.EnableAudio;
        EnableDocuments = profile.EnableDocuments;
        ProbeSeconds = profile.MediaProbeSeconds;
        ValidationDepth = profile.ValidationDepth;
    }

    public ScanOptions BuildOptions(ScanLimitState? limitState = null)
    {
        var db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "media-tools-next", "media-tools-next.db");
        return new ScanOptions(
            SourcePath,
            TargetRoot,
            string.IsNullOrWhiteSpace(BackupRoot) ? null : BackupRoot,
            Mode,
            EnableImages,
            EnableVideo,
            EnableAudio,
            EnableDocuments,
            Concurrency,
            ProbeSeconds,
            db,
            ValidationDepth,
            ExternalToolTimeoutSeconds: ToolTimeoutSeconds,
            MaxMatchedFiles: ZeroAsNull(MaxMatchedFiles),
            MaxMatchedBytes: MbAsBytes(MaxMatchedMb),
            MaxRuntimeSeconds: ZeroAsNull(MaxRuntimeSeconds),
            ForceRescan: ForceRescan,
            LimitState: limitState,
            CustomImageExtensions: CustomImageExtensions,
            CustomImageRegex: string.IsNullOrWhiteSpace(CustomImageRegex) ? null : CustomImageRegex,
            MinCandidateBytes: KbAsBytes(MinFileKb),
            MaxCandidateBytes: MbAsBytes(MaxFileMb),
            IncludeFileNamePatterns: IncludePatterns,
            ExcludeFileNamePatterns: ExcludePatterns,
            ActionStatuses: CopyStatuses);
    }

    public IEnumerable<string> PreflightErrors()
    {
        if (!Directory.Exists(SourcePath)) yield return "Source folder must exist.";
        if (!AnyFamilyEnabled) yield return "Select at least one file family.";
        if (!IsRegexValid(CustomImageRegex)) yield return "Custom image regex is invalid.";
        if (Concurrency is < 1 or > 32) yield return "Concurrency must be between 1 and 32.";
        if (ToolTimeoutSeconds is < 5 or > 600) yield return "Tool timeout must be between 5 and 600 seconds.";
        if (ProbeSeconds is < 10 or > 600) yield return "Media probe seconds must be between 10 and 600.";
        if (MaxRuntimeSeconds < 0) yield return "Max scan time cannot be negative.";
        if (MaxMatchedFiles < 0) yield return "Max matched files cannot be negative.";
        if (MaxMatchedMb < 0) yield return "Max total matched MB cannot be negative.";
        if (MinFileKb < 0) yield return "Minimum file size cannot be negative.";
        if (MaxFileMb < 0) yield return "Maximum file size cannot be negative.";
        if (MaxFileMb > 0 && MinFileKb > MaxFileMb * 1024) yield return "Minimum file size cannot exceed maximum file size.";
        if (Mode != ScanActionMode.DryRun && string.IsNullOrWhiteSpace(TargetRoot)) yield return "Target folder is required for copy modes.";
        if (Mode != ScanActionMode.DryRun && !ConfirmedLiveMode) yield return "Confirm live copy mode before scanning.";
        if (Mode == ScanActionMode.CopySortedAndBackup && string.IsNullOrWhiteSpace(BackupRoot)) yield return "Backup folder is required for backup mode.";
        if (Mode != ScanActionMode.DryRun && CopyStatuses.Count == 0) yield return "Select at least one outcome to copy.";
    }

    public static string DepthLabel(ValidationDepth depth) => depth switch
    {
        ValidationDepth.Fast => "Quick header check",
        ValidationDepth.Standard => "Medium metadata check",
        ValidationDepth.Deep => "In-depth decode check",
        _ => depth.ToString()
    };

    public static string DepthExplanation(ValidationDepth depth) => depth switch
    {
        ValidationDepth.Fast => "Checks file existence, non-zero size, and image header bytes.",
        ValidationDepth.Standard => "Adds ImageMagick identify -ping when available.",
        ValidationDepth.Deep => "Asks ImageMagick to read the image without -ping for stronger corruption detection.",
        _ => string.Empty
    };

    public static bool IsRegexValid(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { _ = System.Text.RegularExpressions.Regex.Match(string.Empty, pattern); return true; }
        catch (ArgumentException) { return false; }
    }

    public static IReadOnlySet<string> ParseExtensions(string text)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split([' ', '\r', '\n', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = raw.StartsWith("*.") ? raw[1..] : raw;
            if (!value.StartsWith('.')) value = "." + value;
            extensions.Add(value.ToLowerInvariant());
        }
        return extensions;
    }

    public static IReadOnlyList<string> ParseList(string text) =>
        text.Split([' ', '\r', '\n', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int? ZeroAsNull(int value) => value <= 0 ? null : value;
    private static long? MbAsBytes(int value) => value <= 0 ? null : value * 1048576L;
    private static long? KbAsBytes(int value) => value <= 0 ? null : value * 1024L;
    private static string FormatLimit(int value, string unit) => value <= 0 ? $"unlimited {unit}" : $"{value:N0} {unit}";
}
