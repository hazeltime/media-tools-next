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
}
