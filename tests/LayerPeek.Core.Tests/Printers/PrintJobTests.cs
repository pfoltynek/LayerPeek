using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

public class PrintJobTests
{
    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(42.5, 42.5)]
    [InlineData(100.0, 100.0)]
    [InlineData(150.0, 100.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 100.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void ProgressPercentIsNormalizedToValidRange(double input, double expected)
    {
        PrintJob job = new() { ProgressPercent = input };

        Assert.Equal(expected, job.ProgressPercent);
    }

    [Fact]
    public void NegativeDurationsAreTreatedAsUnknown()
    {
        PrintJob job = new() { Elapsed = TimeSpan.FromSeconds(-1), Remaining = TimeSpan.FromSeconds(-1) };

        Assert.Null(job.Elapsed);
        Assert.Null(job.Remaining);
    }

    [Fact]
    public void FileNameWhitespaceIsTreatedAsUnknown()
    {
        PrintJob job = new() { FileName = "   " };

        Assert.Null(job.FileName);
    }

    [Fact]
    public void EstimatedRemainingPrefersValueReportedByPrinter()
    {
        PrintJob job = new()
        {
            ProgressPercent = 50,
            Elapsed = TimeSpan.FromMinutes(10),
            Remaining = TimeSpan.FromMinutes(42),
        };

        Assert.Equal(TimeSpan.FromMinutes(42), job.EstimatedRemaining);
    }

    [Theory]
    [InlineData(50.0, 60, 60)]
    [InlineData(25.0, 30, 90)]
    [InlineData(75.0, 90, 30)]
    public void EstimatedRemainingIsExtrapolatedFromProgressWhenNotReported(double progress, int elapsedMinutes, int expectedMinutes)
    {
        PrintJob job = new() { ProgressPercent = progress, Elapsed = TimeSpan.FromMinutes(elapsedMinutes) };

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), job.EstimatedRemaining);
    }

    [Fact]
    public void EstimatedRemainingIsZeroWhenComplete()
    {
        PrintJob job = new() { ProgressPercent = 100, Elapsed = TimeSpan.FromHours(2) };

        Assert.Equal(TimeSpan.Zero, job.EstimatedRemaining);
    }

    [Fact]
    public void EstimatedRemainingIsUnknownWithoutProgress()
    {
        PrintJob job = new() { ProgressPercent = 0, Elapsed = TimeSpan.FromMinutes(5) };

        Assert.Null(job.EstimatedRemaining);
    }

    [Fact]
    public void EstimatedRemainingIsUnknownWithoutElapsedTime()
    {
        PrintJob job = new() { ProgressPercent = 30 };

        Assert.Null(job.EstimatedRemaining);
    }

    [Fact]
    public void EstimatedRemainingIsUnknownWhenExtrapolationOverflows()
    {
        PrintJob job = new() { ProgressPercent = 1e-9, Elapsed = TimeSpan.MaxValue };

        Assert.Null(job.EstimatedRemaining);
    }

    [Fact]
    public void EqualityIsValueBased()
    {
        PrintJob first = new() { FileName = "benchy.bgcode", ProgressPercent = 10, Elapsed = TimeSpan.FromMinutes(1) };
        PrintJob second = new() { FileName = "benchy.bgcode", ProgressPercent = 10, Elapsed = TimeSpan.FromMinutes(1) };

        Assert.Equal(first, second);
    }
}
