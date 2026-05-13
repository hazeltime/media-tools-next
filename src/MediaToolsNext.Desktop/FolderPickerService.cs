using Ookii.Dialogs.WinForms;

namespace MediaToolsNext.Desktop;

public sealed class FolderPickerService
{
    public Task<string?> PickFolderAsync(string title, string? initialPath)
    {
#if WINDOWS
        using var dialog = new VistaFolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initialPath) ? initialPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        return Task.FromResult(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null);
#else
        return Task.FromResult<string?>(null);
#endif
    }

    public Task<string?> PickCsvSavePathAsync(string initialFileName)
    {
#if WINDOWS
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = initialFileName,
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            RestoreDirectory = true,
            OverwritePrompt = true,
            Title = "Export scan results"
        };

        return Task.FromResult(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null);
#else
        return Task.FromResult<string?>(null);
#endif
    }
}
