namespace MediaToolsNext.Desktop;

public sealed class FolderPickerService
{
    public Task<string?> PickFolderAsync(string title, string? initialPath)
    {
        // Headless folder picker mock returning a clean temp path for automated visual test execution
        var virtualPath = Path.Combine(Path.GetTempPath(), "media-tools-next-web-source");
        Directory.CreateDirectory(virtualPath);
        return Task.FromResult<string?>(virtualPath);
    }

    public Task<string?> PickCsvSavePathAsync(string initialFileName)
    {
        var virtualPath = Path.Combine(Path.GetTempPath(), initialFileName);
        return Task.FromResult<string?>(virtualPath);
    }
}
