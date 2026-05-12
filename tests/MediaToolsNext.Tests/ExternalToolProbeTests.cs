using MediaToolsNext.Infrastructure;

namespace MediaToolsNext.Tests;

public class ExternalToolProbeTests
{
    [Fact]
    public void StatusesAreCachedForProcessLifetime()
    {
        var probe = new ExternalToolProbe();
        Assert.Same(probe.GetStatuses(), probe.GetStatuses());
    }
}
