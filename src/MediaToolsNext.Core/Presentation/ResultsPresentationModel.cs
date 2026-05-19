using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaToolsNext.Core.Presentation;

public sealed class ResultsPresentationModel
{
    private readonly Func<IReadOnlyList<ScanResultRecord>> _getResults;

    public string ActiveTab { get; set; } = "all";
    public string SearchText { get; set; } = string.Empty;
    public string DetailGroup { get; set; } = string.Empty;
    public string SortOrder { get; set; } = "status-path";
    public int Page { get; set; } = 0;
    public int PageSize { get; set; } = 50;

    public List<ScanResultRecord> VisibleRows { get; private set; } = [];
    public List<ScanResultRecord> PagedRows { get; private set; } = [];
    public List<ScanResultRecord> ImageRows { get; private set; } = [];
    public int FilteredCount { get; private set; } = 0;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)PageSize));
    public int PageStart => Page * PageSize;

    public HashSet<string> SelectedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ResultsPresentationModel(Func<IReadOnlyList<ScanResultRecord>> getResults)
    {
        _getResults = getResults ?? throw new ArgumentNullException(nameof(getResults));
    }

    public void RebuildCache()
    {
        var allResults = _getResults();

        if (ActiveTab is "searched" or "filtered-size" or "filtered-pattern" or "filtered-family")
        {
            FilteredCount = 0;
            VisibleRows = [];
            PagedRows = [];
            ImageRows = [];
            return;
        }

        ValidationStatus? statusFilter = ActiveTab switch
        {
            "valid" => ValidationStatus.Valid,
            "corrupt" => ValidationStatus.Corrupt,
            "unknown" => ValidationStatus.Unknown,
            "error" => ValidationStatus.Error,
            "skipped" => ValidationStatus.Skipped,
            _ => null
        };

        var sorted = SortOrder switch
        {
            "path" => allResults.OrderBy(x => x.Candidate.RelativePath),
            "size-desc" => allResults.OrderByDescending(x => x.Candidate.SizeBytes),
            "status-path" => allResults.OrderBy(x => x.Status).ThenBy(x => x.Candidate.RelativePath),
            "detail" => allResults.OrderBy(x => NormalizeDetail(x.Detail)).ThenBy(x => x.Candidate.RelativePath),
            "validator" => allResults.OrderBy(x => x.Validator).ThenBy(x => x.Candidate.RelativePath),
            "newest" => allResults.OrderByDescending(x => x.TimestampUtc),
            _ => (IEnumerable<ScanResultRecord>)allResults
        };

        var filtered = sorted.Where(r =>
        {
            if (statusFilter.HasValue && r.Status != statusFilter.Value) return false;
            if (!string.IsNullOrWhiteSpace(DetailGroup) && !NormalizeDetail(r.Detail).Equals(DetailGroup, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            return r.Candidate.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || r.Candidate.FullPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (r.Detail?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.Validator.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        FilteredCount = filtered.Count;
        VisibleRows = filtered;
        Page = Math.Clamp(Page, 0, TotalPages - 1);
        PagedRows = VisibleRows.Skip(PageStart).Take(PageSize).ToList();
        ImageRows = VisibleRows.Where(r => r.Candidate.Category == MediaCategory.Image).ToList();
    }

    public static string RowKey(ScanResultRecord row) => row.Candidate.FullPath + "|" + row.TimestampUtc.ToUnixTimeMilliseconds();

    public bool IsPageSelected => PagedRows.Count > 0 && PagedRows.All(r => SelectedKeys.Contains(RowKey(r)));

    public void ToggleRow(ScanResultRecord row, bool selected)
    {
        var key = RowKey(row);
        if (selected) SelectedKeys.Add(key);
        else SelectedKeys.Remove(key);
    }

    public void TogglePage(bool selected)
    {
        foreach (var row in PagedRows)
            ToggleRow(row, selected);
    }

    public void SelectPage()
    {
        foreach (var row in PagedRows)
            SelectedKeys.Add(RowKey(row));
    }

    public void SelectFiltered()
    {
        foreach (var row in VisibleRows)
            SelectedKeys.Add(RowKey(row));
    }

    public void ClearSelection() => SelectedKeys.Clear();

    public void PreviousPage()
    {
        if (Page > 0) Page--;
        RebuildCache();
    }

    public void NextPage()
    {
        if (Page < TotalPages - 1) Page++;
        RebuildCache();
    }

    public void ResetPage()
    {
        Page = 0;
        RebuildCache();
    }

    public IReadOnlyList<string> GetDetailGroups()
    {
        return _getResults()
            .Select(r => NormalizeDetail(r.Detail))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1_048_576) return (bytes / 1_048_576d).ToString("N1") + " MB";
        if (bytes >= 1_024) return (bytes / 1_024d).ToString("N1") + " KB";
        return bytes + " B";
    }

    public static string FormatTime(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    public static string FormatPercent(double value, double total) => total <= 0 ? "n/a" : $"{Math.Clamp(value / total, 0, 1):P0}";
    public static string NormalizeDetail(string? detail) => string.IsNullOrWhiteSpace(detail) ? "No detail" : detail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "No detail";
}
