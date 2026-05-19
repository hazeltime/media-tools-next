using System.Threading.Tasks;

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
    public static class Clipboard
    {
        public static ClipboardShim Default { get; } = new ClipboardShim();
    }

    public class ClipboardShim
    {
        public Task SetTextAsync(string text)
        {
            // Headless clipboard stub for web environments
            return Task.CompletedTask;
        }
    }
}
