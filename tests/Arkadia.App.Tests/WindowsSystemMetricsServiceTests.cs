using Arkadia.App.Services;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class WindowsSystemMetricsServiceTests
{
    [Fact]
    public void ReadReturnsSystemMemoryAndCpuUsage()
    {
        var service = new WindowsSystemMetricsService();
        var firstSample = service.Read();
        Thread.Sleep(TimeSpan.FromMilliseconds(100));
        var secondSample = service.Read();

        Assert.InRange(firstSample.MemoryUsagePercent, 0d, 100d);
        Assert.NotNull(secondSample.CpuUsagePercent);
        Assert.InRange(secondSample.CpuUsagePercent!.Value, 0d, 100d);
        Assert.InRange(secondSample.MemoryUsagePercent, 0d, 100d);
    }
}
