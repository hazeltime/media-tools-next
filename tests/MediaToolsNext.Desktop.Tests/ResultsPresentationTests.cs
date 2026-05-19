using System;
using System.Collections.Generic;
using System.Linq;
using MediaToolsNext.Core;
using MediaToolsNext.Core.Presentation;
using Xunit;

namespace MediaToolsNext.Desktop.Tests;

public class ResultsPresentationTests
{
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".svg", "image/svg+xml")]
    [InlineData(".mp4", "application/octet-stream")]
    [InlineData("", "application/octet-stream")]
    [InlineData(null, "application/octet-stream")]
    public void ImagePreviewHelperMimeTypes(string? extension, string expectedMime)
    {
        Assert.Equal(expectedMime, ImagePreviewHelper.ResolveMimeType(extension));
    }

    [Fact]
    public void ImagePreviewHelperCanPreviewLimits()
    {
        Assert.True(ImagePreviewHelper.CanPreview(100));
        Assert.True(ImagePreviewHelper.CanPreview(10 * 1024 * 1024)); // exactly 10MB
        Assert.False(ImagePreviewHelper.CanPreview(10 * 1024 * 1024 + 1)); // >10MB
    }

    [Fact]
    public void FormatBytesHelperFormatsCorrectly()
    {
        var decSep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal($"10{decSep}0 MB", ResultsPresentationModel.FormatBytes(10 * 1024 * 1024));
        Assert.Equal($"512{decSep}0 KB", ResultsPresentationModel.FormatBytes(512 * 1024));
        Assert.Equal("123 B", ResultsPresentationModel.FormatBytes(123));
    }

    [Fact]
    public void ModelFiltersAndSortsCorrectly()
    {
        var records = new List<ScanResultRecord>
        {
            CreateRecord("valid_a.jpg", MediaCategory.Image, 1024, ValidationStatus.Valid, "Detail Valid A", "ImageVal"),
            CreateRecord("corrupt_b.mp4", MediaCategory.Video, 2048, ValidationStatus.Corrupt, "Detail Corrupt B", "VideoVal"),
            CreateRecord("unknown_c.pdf", MediaCategory.Document, 512, ValidationStatus.Unknown, "Detail Unknown C", "DocVal"),
            CreateRecord("error_d.png", MediaCategory.Image, 4096, ValidationStatus.Error, "Detail Error D", "ImageVal"),
        };

        var model = new ResultsPresentationModel(() => records);

        // Test Default State
        model.ActiveTab = "all";
        model.SortOrder = "path";
        model.RebuildCache();

        Assert.Equal(4, model.FilteredCount);
        Assert.Equal("corrupt_b.mp4", model.VisibleRows[0].Candidate.RelativePath);
        Assert.Equal("error_d.png", model.VisibleRows[1].Candidate.RelativePath);

        // Test Status Filtering (corrupt tab)
        model.ActiveTab = "corrupt";
        model.RebuildCache();
        Assert.Single(model.VisibleRows);
        Assert.Equal("corrupt_b.mp4", model.VisibleRows[0].Candidate.RelativePath);

        // Test Text Search Filtering
        model.ActiveTab = "all";
        model.SearchText = "png";
        model.RebuildCache();
        Assert.Single(model.VisibleRows);
        Assert.Equal("error_d.png", model.VisibleRows[0].Candidate.RelativePath);

        // Test Detail Group filtering
        model.SearchText = "";
        model.DetailGroup = "Detail Unknown C";
        model.RebuildCache();
        Assert.Single(model.VisibleRows);
        Assert.Equal("unknown_c.pdf", model.VisibleRows[0].Candidate.RelativePath);

        // Test Sorting by size (descending)
        model.DetailGroup = "";
        model.SortOrder = "size-desc";
        model.RebuildCache();
        Assert.Equal("error_d.png", model.VisibleRows[0].Candidate.RelativePath); // 4096
        Assert.Equal("corrupt_b.mp4", model.VisibleRows[1].Candidate.RelativePath); // 2048
    }

    [Fact]
    public void ModelSelectionTrackingWorks()
    {
        var records = new List<ScanResultRecord>
        {
            CreateRecord("a.jpg", MediaCategory.Image, 100, ValidationStatus.Valid),
            CreateRecord("b.jpg", MediaCategory.Image, 100, ValidationStatus.Valid),
            CreateRecord("c.jpg", MediaCategory.Image, 100, ValidationStatus.Valid)
        };

        var model = new ResultsPresentationModel(() => records)
        {
            PageSize = 2
        };
        model.RebuildCache();

        // 3 items, page size 2 means 2 pages
        Assert.Equal(2, model.TotalPages);

        // Initial Selection State
        Assert.Empty(model.SelectedKeys);
        Assert.False(model.IsPageSelected);

        // Toggle a single row
        var first = model.PagedRows[0];
        model.ToggleRow(first, true);
        Assert.Single(model.SelectedKeys);

        // Toggle page (will toggle remaining row)
        model.TogglePage(true);
        Assert.Equal(2, model.SelectedKeys.Count);
        Assert.True(model.IsPageSelected);

        // Clear Selection
        model.ClearSelection();
        Assert.Empty(model.SelectedKeys);

        // Select Filtered (which is all 3 rows since no filter applied)
        model.SelectFiltered();
        Assert.Equal(3, model.SelectedKeys.Count);
    }

    [Fact]
    public void ModelPagingNavigationWorks()
    {
        var records = Enumerable.Range(0, 15)
            .Select(i => CreateRecord($"file_{i}.jpg", MediaCategory.Image, 100, ValidationStatus.Valid))
            .ToList();

        var model = new ResultsPresentationModel(() => records)
        {
            PageSize = 5
        };
        model.RebuildCache();

        Assert.Equal(3, model.TotalPages);
        Assert.Equal(0, model.Page);
        Assert.Equal(5, model.PagedRows.Count);

        model.NextPage();
        Assert.Equal(1, model.Page);

        model.PreviousPage();
        Assert.Equal(0, model.Page);

        model.Page = 10; // set out of bounds
        model.RebuildCache();
        Assert.Equal(2, model.Page); // clamped to max page (index 2)
    }

    private static ScanResultRecord CreateRecord(
        string relativePath,
        MediaCategory category,
        long sizeBytes,
        ValidationStatus status,
        string detail = "No detail",
        string validator = "test-val")
    {
        var candidate = new FileCandidate("/mock/" + relativePath, relativePath, Path.GetExtension(relativePath), category, sizeBytes, DateTimeOffset.UtcNow);
        return new ScanResultRecord(
            Guid.NewGuid(),
            candidate,
            status,
            validator,
            detail,
            "copied",
            "primary-target",
            "backup-target",
            DateTimeOffset.UtcNow);
    }
}
