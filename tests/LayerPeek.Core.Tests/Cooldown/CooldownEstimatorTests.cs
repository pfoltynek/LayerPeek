using LayerPeek.Core.Cooldown;

using static LayerPeek.Core.Tests.Cooldown.CooldownTestData;

namespace LayerPeek.Core.Tests.Cooldown;

public sealed class CooldownEstimatorTests
{
    private const double Threshold = 50;

    private readonly CooldownEstimator _estimator = new();

    public static TheoryData<int> Thresholds => [30, 40, 50, 70, 100];

    [Theory]
    [MemberData(nameof(Thresholds))]
    public void ConvergesOnTheSimulatorCurve(int threshold) =>
        AssertConverges(threshold, static t => SimulatedNozzle(t), maxRelativeError: 0.2);

    [Theory]
    [MemberData(nameof(Thresholds))]
    public void ConvergesOnTheSimulatorCurveWithItsWobble(int threshold) =>
        AssertConverges(threshold, static t => Wobble(SimulatedNozzle(t), t), maxRelativeError: 0.2);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ConvergesOnANoisyCurve(int seed)
    {
        Random random = new(seed);
        // ±0.5 °C of uniform noise, about what a thermistor reading shows.
        AssertConverges(Threshold, t => SimulatedNozzle(t) + (random.NextDouble() - 0.5), maxRelativeError: 0.2, minRemainingSeconds: 15);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void ConvergesWithOtherPollingIntervals(double pollSeconds) =>
        AssertConverges(Threshold, static t => SimulatedNozzle(t), maxRelativeError: 0.2, pollSeconds: pollSeconds);

    [Fact]
    public void ConvergesOnASlowerNozzleInAWarmRoom()
    {
        // τ = 240 s towards 35 °C, a printer in an enclosure without a part fan.
        static double Curve(double t) => 35 + (180 * Math.Exp(-t / 240));
        double reach = 240 * Math.Log(180 / 15.0);

        for (double t = 0; t < reach; t += PollSeconds)
        {
            _estimator.Add(Start.AddSeconds(t), Curve(t));
            TimeSpan? estimate = _estimator.EstimateRemaining(Threshold);
            if (t >= reach / 2)
            {
                double actual = reach - t;
                Assert.NotNull(estimate);
                Assert.InRange(Math.Abs(estimate.Value.TotalSeconds - actual), 0, Math.Max(0.2 * actual, PollSeconds / 2));
            }
        }
    }

    [Fact]
    public void EstimatesAreStableFromPollToPoll()
    {
        double reach = SecondsToReach(Threshold);
        double? previous = null;
        for (double t = 0; t < reach; t += PollSeconds)
        {
            _estimator.Add(Start.AddSeconds(t), Wobble(SimulatedNozzle(t), t));
            if (_estimator.EstimateRemaining(Threshold) is not { } estimate)
            {
                continue;
            }

            if (previous is { } last && t >= reach / 3)
            {
                // From one poll to the next the estimate counts down by about the poll interval.
                Assert.InRange(last - estimate.TotalSeconds, 0, 2 * PollSeconds);
            }

            previous = estimate.TotalSeconds;
        }

        Assert.NotNull(previous);
    }

    [Fact]
    public void TooFewSamplesGiveNoEstimate()
    {
        for (int i = 0; i < CooldownEstimator.MinSamples - 1; i++)
        {
            _estimator.Add(Start.AddSeconds(i * PollSeconds), SimulatedNozzle(i * PollSeconds));
            Assert.Null(_estimator.EstimateRemaining(Threshold));
        }
    }

    [Fact]
    public void TooShortSpanGivesNoEstimate()
    {
        // Ten samples within 18 s (1 s polling is thinned out to the minimum spacing).
        for (int i = 0; i < 19; i++)
        {
            _estimator.Add(Start.AddSeconds(i), SimulatedNozzle(i));
        }

        Assert.Null(_estimator.EstimateRemaining(Threshold));
    }

    [Fact]
    public void RisingTemperatureGivesNoEstimate()
    {
        for (int i = 0; i < 30; i++)
        {
            _estimator.Add(Start.AddSeconds(i * PollSeconds), 60 + i);
        }

        Assert.Null(_estimator.EstimateRemaining(Threshold));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoiseWithoutTrendGivesNoEstimate(int seed)
    {
        Random random = new(seed);
        for (int i = 0; i < 40; i++)
        {
            _estimator.Add(Start.AddSeconds(i * PollSeconds), 120 + ((random.NextDouble() - 0.5) * 3));
            Assert.Null(_estimator.EstimateRemaining(Threshold));
        }
    }

    [Fact]
    public void CurveLevellingOffAboveTheThresholdGivesNoEstimate()
    {
        // The nozzle approaches 60 °C (e.g. a heated chamber) and never reaches 50 °C.
        for (int i = 0; i < 40; i++)
        {
            double t = i * PollSeconds;
            _estimator.Add(Start.AddSeconds(t), 60 + (150 * Math.Exp(-t / 60)));
        }

        Assert.Null(_estimator.EstimateRemaining(Threshold));
    }

    [Fact]
    public void AtOrBelowTheThresholdTheEstimateIsZero()
    {
        for (double t = 0; t <= 200; t += PollSeconds)
        {
            _estimator.Add(Start.AddSeconds(t), SimulatedNozzle(t));
        }

        Assert.Equal(TimeSpan.Zero, _estimator.EstimateRemaining(Threshold));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ArbitraryDataNeverGivesANegativeOrInfiniteEstimate(int seed)
    {
        Random random = new(seed);
        DateTimeOffset time = Start;
        for (int i = 0; i < 2000; i++)
        {
            time = time.AddSeconds(random.Next(-5, 120));
            double value = random.Next(5) switch
            {
                0 => random.NextDouble() * 300,
                1 => 200 - (i % 50 * 3) + random.NextDouble(),
                2 => 50 + (random.NextDouble() * 0.001),
                3 => i % 2 == 0 ? 0 : 400,
                _ => 23 + (180 * Math.Exp(-(i % 40) / 3.0)),
            };
            _estimator.Add(time, value);
            int threshold = random.Next(CooldownThreshold.MinCelsius, CooldownThreshold.MaxCelsius + 1);

            if (_estimator.EstimateRemaining(threshold) is { } estimate)
            {
                Assert.True(estimate >= TimeSpan.Zero, $"Negative estimate {estimate}.");
                Assert.True(estimate <= CooldownEstimator.MaxEstimate, $"Estimate {estimate} is too long.");
            }

            Assert.InRange(_estimator.Count, 0, CooldownEstimator.Capacity);
        }
    }

    [Fact]
    public void MemoryIsBounded()
    {
        for (int i = 0; i < 10_000; i++)
        {
            _estimator.Add(Start.AddSeconds(i * PollSeconds), 200 - (i * 0.01));
        }

        Assert.Equal(CooldownEstimator.Capacity, _estimator.Count);
    }

    [Fact]
    public void SamplesCloserThanTheMinimumSpacingAreSkipped()
    {
        _estimator.Add(Start, 200);
        _estimator.Add(Start, 199);
        _estimator.Add(Start.AddSeconds(0.5), 198);

        Assert.Equal(1, _estimator.Count);
    }

    [Fact]
    public void ClockGoingBackStartsOver()
    {
        for (int i = 0; i < 20; i++)
        {
            _estimator.Add(Start.AddSeconds(i * PollSeconds), SimulatedNozzle(i * PollSeconds));
        }

        _estimator.Add(Start.AddHours(-1), 100);

        Assert.Equal(1, _estimator.Count);
        Assert.Null(_estimator.EstimateRemaining(Threshold));
    }

    [Fact]
    public void ClearForgetsAllSamples()
    {
        for (int i = 0; i < 20; i++)
        {
            _estimator.Add(Start.AddSeconds(i * PollSeconds), SimulatedNozzle(i * PollSeconds));
        }

        _estimator.Clear();

        Assert.Equal(0, _estimator.Count);
        Assert.Null(_estimator.EstimateRemaining(Threshold));
    }

    [Fact]
    public void GapsInTheSamplesAreHandled()
    {
        double reach = SecondsToReach(Threshold);
        for (double t = 0; t < reach; t += PollSeconds)
        {
            // Every third poll had no reading, and a whole stretch was lost.
            if ((int)(t / PollSeconds) % 3 == 1 || t is > 30 and < 55)
            {
                continue;
            }

            _estimator.Add(Start.AddSeconds(t), SimulatedNozzle(t));
            if (t >= reach / 2)
            {
                double actual = reach - t;
                TimeSpan? estimate = _estimator.EstimateRemaining(Threshold);
                Assert.NotNull(estimate);
                Assert.InRange(estimate.Value.TotalSeconds, actual * 0.8, actual * 1.2);
            }
        }
    }

    /// <summary>
    /// Feeds <paramref name="curve"/> (temperature by seconds since the heater was switched off) until the threshold and
    /// checks that the estimate is within <paramref name="maxRelativeError"/> of the true remaining time in the second
    /// half of the cool-down.
    /// </summary>
    private void AssertConverges(double threshold, Func<double, double> curve, double maxRelativeError, double minRemainingSeconds = 0, double pollSeconds = PollSeconds)
    {
        double reach = SecondsToReach(threshold);
        int checkedEstimates = 0;
        for (double t = 0; t < reach; t += pollSeconds)
        {
            _estimator.Add(Start.AddSeconds(t), curve(t));
            // Measured from now: with 1 s polling the last kept reading may be a second older.
            TimeSpan? estimate = _estimator.EstimateRemaining(threshold, Start.AddSeconds(t));
            if (estimate is { } value)
            {
                Assert.True(value >= TimeSpan.Zero);
            }

            double actual = reach - t;
            if (t >= reach / 2 && actual >= minRemainingSeconds)
            {
                Assert.True(estimate is not null, $"No estimate at {t} s.");
                double error = Math.Abs(estimate.Value.TotalSeconds - actual);
                // Within the poll interval at the very end, where 20 % is less than the sampling resolution.
                Assert.True(
                    error <= Math.Max(maxRelativeError * actual, pollSeconds / 2),
                    $"At {t:0} s: estimate {estimate.Value.TotalSeconds:0.0} s, actual {actual:0.0} s.");
                checkedEstimates++;
            }
        }

        Assert.True(checkedEstimates > 0,"Nothing was checked.");
    }
}
