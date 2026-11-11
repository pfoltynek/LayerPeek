using LayerPeek.Core.Monitoring;

namespace LayerPeek.Core.Tests.Monitoring;

public sealed class ExponentialBackoffTests
{
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(5, 60)]
    [InlineData(6, 60)]
    [InlineData(int.MaxValue, 60)]
    public void DoublesFromTheInitialDelayUpToTheCap(int consecutiveFailures, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ExponentialBackoff.Delay(Initial, Max, consecutiveFailures));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void CapNotAboveTheInitialDelayMeansNoBackoff(int consecutiveFailures)
    {
        Assert.Equal(Initial, ExponentialBackoff.Delay(Initial, Initial, consecutiveFailures));
        Assert.Equal(Initial, ExponentialBackoff.Delay(Initial, TimeSpan.FromSeconds(1), consecutiveFailures));
    }

    [Fact]
    public void HugeCapDoesNotOverflow() =>
        Assert.Equal(TimeSpan.MaxValue, ExponentialBackoff.Delay(TimeSpan.FromTicks(1), TimeSpan.MaxValue, int.MaxValue));
}
