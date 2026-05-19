using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ToolInstallServiceTests
{
    [Fact]
    public async Task InstallFallsBackToWingetWhenChocoFails()
    {
        var runner = new FakeInstallRunner(
            new(false, "choco install ffmpeg -y --no-progress", "choco failed"),
            new(true, "winget install --id Gyan.FFmpeg --silent --accept-source-agreements --accept-package-agreements", "winget ok"));
        var service = new ToolInstallService(runner, TimeSpan.FromSeconds(5));

        var result = await service.InstallAsync("ffmpeg", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("winget install --id Gyan.FFmpeg --silent --accept-source-agreements --accept-package-agreements", result.Command);
        Assert.Equal(["choco", "winget"], runner.Calls.Select(call => call.FileName));
        Assert.All(runner.Calls, call => Assert.Equal(TimeSpan.FromSeconds(5), call.Timeout));
    }

    [Fact]
    public async Task InstallReturnsMappingFailureForUnknownTool()
    {
        var runner = new FakeInstallRunner();
        var service = new ToolInstallService(runner);

        var result = await service.InstallAsync("unknown", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(runner.Calls);
        Assert.Contains("No installer package mapping", result.Output);
    }

    [Fact]
    public async Task UpgradeAllRunsEachDistinctToolOnce()
    {
        var runner = new FakeInstallRunner(
            new(true, "choco upgrade ffmpeg -y --no-progress", "ok"),
            new(true, "choco upgrade qpdf -y --no-progress", "ok"));
        var service = new ToolInstallService(runner);

        var result = await service.UpgradeAllAsync(["ffmpeg", "FFMPEG", "qpdf"], CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["ffmpeg", "qpdf"], runner.Calls.Select(call => call.Arguments[1]));
    }

    private sealed class FakeInstallRunner(params ToolInstallResult[] results) : IToolInstallProcessRunner
    {
        private readonly Queue<ToolInstallResult> _results = new(results);
        public List<InstallCall> Calls { get; } = [];

        public Task<ToolInstallResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(new(fileName, arguments.ToArray(), timeout));
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new ToolInstallResult(false, fileName + " " + string.Join(" ", arguments), "failed"));
        }
    }

    private sealed record InstallCall(string FileName, string[] Arguments, TimeSpan Timeout);
}
