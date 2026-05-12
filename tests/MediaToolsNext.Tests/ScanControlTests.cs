using MediaToolsNext.Core;

namespace MediaToolsNext.Tests;

public class ScanControlTests
{
    [Fact]
    public async Task WaitBlocksUntilResume()
    {
        var control = new ScanControl();
        control.Pause();
        var wait = control.WaitIfPausedAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);
        control.Resume();
        await wait;
    }
}
