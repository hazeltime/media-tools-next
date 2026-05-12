using Ookii.Dialogs.WinForms;

namespace MediaToolsNext.Desktop;

public sealed class FolderPickerService
{
    public Task<string?> PickFolderAsync(string title, string? initialPath)
    {
        using var dialog = new VistaFolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initialPath) ? initialPath : string.Empty
        };

        return Task.FromResult(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null);
    }

    public Task<string?> PickCsvSavePathAsync(string initialFileName)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "csv",
            FileName = initialFileName,
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Export scan results"
        };

        return Task.FromResult(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null);
    }
}
