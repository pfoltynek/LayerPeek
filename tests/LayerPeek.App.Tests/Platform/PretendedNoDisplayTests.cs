#if DEBUG
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform;

/// <summary>T-099: the Debug test switch reports no display for its time, then the real displays; fakes only.</summary>
public sealed class PretendedNoDisplayTests
{
    [Fact]
    public void NoDisplayForTheTimeThenTheRealCount()
    {
        FakeTimeProvider clock = new();
        Real real = new();
        ListLogger<PretendedNoDisplay> logger = new();
        PretendedNoDisplay displays = new(real, TimeSpan.FromSeconds(10), clock, logger);

        Assert.True(displays.TryCount(out uint count, out int error));
        Assert.Equal((0u, 0), (count, error));
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.True(displays.TryCount(out count, out _));
        Assert.Equal(0u, count);
        Assert.Equal(0, real.Calls);
        Assert.Single(logger.Entries);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(displays.TryCount(out count, out _));
        Assert.Equal(2u, count);
        Assert.Equal(1, real.Calls);
    }

    private sealed class Real : IActiveDisplays
    {
        public int Calls { get; private set; }

        public bool TryCount(out uint count, out int error)
        {
            Calls++;
            count = 2;
            error = 0;
            return true;
        }
    }
}
#endif
