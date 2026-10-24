namespace LayerPeek.Core.Cooldown;

/// <summary>
/// Estimates when a cooling nozzle reaches a threshold from its recent readings, assuming Newton's law of cooling:
/// <c>T(t) = A + C·e^(−k·t)</c> with unknown ambient temperature <c>A</c>, amplitude <c>C</c> and rate <c>k = 1/τ</c>.
/// <para>
/// Fit: for a fixed <c>k</c> the model is linear in <c>A</c> and <c>C</c>, which are solved in closed form (least
/// squares with a weak prior pulling <c>A</c> towards <see cref="AmbientPriorCelsius"/>, so that the short, almost
/// straight start of a cool-down, where ambient and τ cannot be told apart, gives plausible values). The sum of squared
/// residuals is then minimized over <c>ln k</c> (τ within <see cref="MinTauSeconds"/>–<see cref="MaxTauSeconds"/>) by a
/// coarse grid followed by a golden-section search (variable projection). Unequal spacing and gaps are handled exactly.
/// </para>
/// <para>
/// No estimate (<see langword="null"/>) with fewer than <see cref="MinSamples"/> readings or a span shorter than
/// <see cref="MinSpanSeconds"/>, when the readings do not fall by at least <see cref="MinDropCelsius"/>, when the best
/// τ lies at the edge of the range, when the residuals exceed <see cref="MaxNoiseFraction"/> of the fitted drop, when
/// the fitted ambient is not below the threshold, or when the result exceeds <see cref="MaxEstimate"/>. A result is
/// never negative.
/// </para>
/// Keeps at most <see cref="Capacity"/> readings at least <see cref="MinSpacingSeconds"/> apart in fixed arrays; adding
/// and estimating allocate nothing. Not thread-safe.
/// </summary>
internal sealed class CooldownEstimator
{
    public const int Capacity = 48;

    public const int MinSamples = 6;

    public const double MinSpacingSeconds = 2;

    public const double MinSpanSeconds = 20;

    public const double MinDropCelsius = 2;

    public const double MaxNoiseFraction = 0.2;

    public const double AmbientPriorCelsius = 25;

    /// <summary>Weight of the ambient prior relative to one reading: about (1 °C noise / 10 °C prior uncertainty)².</summary>
    public const double AmbientPriorWeight = 0.01;

    public const double MinTauSeconds = 5;

    public const double MaxTauSeconds = 4 * 3600;

    public static readonly TimeSpan MaxEstimate = TimeSpan.FromHours(12);

    private const int GridSteps = 24;

    private const int RefineIterations = 30;

    private static readonly double GoldenRatio = (Math.Sqrt(5) - 1) / 2;

    private static readonly long MinSpacingTicks = (long)(MinSpacingSeconds * TimeSpan.TicksPerSecond);

    // Ring buffer of readings, oldest at _start.
    private readonly long[] _ticks = new long[Capacity];
    private readonly double[] _values = new double[Capacity];

    // Scratch space of a fit: seconds since the oldest reading, readings and e^(−k·t), oldest first.
    private readonly double[] _t = new double[Capacity];
    private readonly double[] _y = new double[Capacity];
    private readonly double[] _x = new double[Capacity];

    private int _start;

    public int Count { get; private set; }

    public void Clear()
    {
        _start = 0;
        Count = 0;
    }

    /// <summary>
    /// Adds a reading. Readings closer than <see cref="MinSpacingSeconds"/> to the previous one (or at the same time) are
    /// skipped; a reading earlier than the previous one (the clock went back) starts over; non-finite values are ignored.
    /// </summary>
    public void Add(DateTimeOffset timestamp, double celsius)
    {
        if (!double.IsFinite(celsius))
        {
            return;
        }

        long ticks = timestamp.UtcTicks;
        if (Count > 0)
        {
            long last = _ticks[Index(Count - 1)];
            if (ticks < last)
            {
                Clear();
            }
            else if (ticks - last < MinSpacingTicks)
            {
                return;
            }
        }

        if (Count == Capacity)
        {
            _start = Index(1);
            Count--;
        }

        int index = Index(Count);
        _ticks[index] = ticks;
        _values[index] = celsius;
        Count++;
    }

    /// <summary>
    /// Time until the nozzle reaches <paramref name="thresholdCelsius"/>, from <paramref name="at"/> (default: the last
    /// reading); <see cref="TimeSpan.Zero"/> when the fitted curve is already there; <see langword="null"/> without a
    /// reliable estimate (see the class remarks).
    /// </summary>
    public TimeSpan? EstimateRemaining(double thresholdCelsius, DateTimeOffset? at = null)
    {
        int n = Count;
        if (n < MinSamples || !double.IsFinite(thresholdCelsius))
        {
            return null;
        }

        long firstTicks = _ticks[_start];
        for (int i = 0; i < n; i++)
        {
            int index = Index(i);
            _t[i] = (double)(_ticks[index] - firstTicks) / TimeSpan.TicksPerSecond;
            _y[i] = _values[index];
        }

        double span = _t[n - 1];
        if (span < MinSpanSeconds || ObservedDrop(n) < MinDropCelsius)
        {
            return null;
        }

        if (!TryFit(n, out double ambient, out double amplitude, out double rate, out double residualSquares))
        {
            return null;
        }

        double endFactor = Math.Exp(-rate * span);
        double fittedDrop = amplitude * (1 - endFactor);
        if (!(amplitude > 0) || !(fittedDrop >= MinDropCelsius) || Math.Sqrt(residualSquares / n) > MaxNoiseFraction * fittedDrop)
        {
            return null;
        }

        double current = ambient + (amplitude * endFactor);
        double seconds;
        if (current <= thresholdCelsius)
        {
            seconds = 0;
        }
        else if (ambient >= thresholdCelsius)
        {
            // Levels off above the threshold: never gets there.
            return null;
        }
        else
        {
            seconds = Math.Log((current - ambient) / (thresholdCelsius - ambient)) / rate;
        }

        if (at is { } now)
        {
            seconds -= (double)(now.UtcTicks - _ticks[Index(n - 1)]) / TimeSpan.TicksPerSecond;
        }

        if (!double.IsFinite(seconds) || seconds > MaxEstimate.TotalSeconds)
        {
            return null;
        }

        return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
    }

    private int Index(int offset) => (_start + offset) % Capacity;

    /// <summary>Mean of the oldest quarter minus mean of the newest quarter of the readings (robust against single noisy values).</summary>
    private double ObservedDrop(int n)
    {
        int quarter = Math.Max(1, n / 4);
        double head = 0;
        double tail = 0;
        for (int i = 0; i < quarter; i++)
        {
            head += _y[i];
            tail += _y[n - 1 - i];
        }

        return (head - tail) / quarter;
    }

    private bool TryFit(int n, out double ambient, out double amplitude, out double rate, out double residualSquares)
    {
        double low = Math.Log(1 / MaxTauSeconds);
        double high = Math.Log(1 / MinTauSeconds);
        double step = (high - low) / GridSteps;

        int best = -1;
        double bestError = double.PositiveInfinity;
        for (int i = 0; i <= GridSteps; i++)
        {
            double error = Evaluate(n, low + (i * step), out _, out _, out _);
            if (error < bestError)
            {
                bestError = error;
                best = i;
            }
        }

        ambient = amplitude = rate = residualSquares = 0;
        if (best <= 0 || best >= GridSteps)
        {
            // No fit, or the best τ is at the edge of the plausible range: the data do not determine it.
            return false;
        }

        // Golden-section search between the neighbours of the best grid point.
        double a = low + ((best - 1) * step);
        double b = low + ((best + 1) * step);
        double c = b - (GoldenRatio * (b - a));
        double d = a + (GoldenRatio * (b - a));
        double errorC = Evaluate(n, c, out _, out _, out _);
        double errorD = Evaluate(n, d, out _, out _, out _);
        for (int i = 0; i < RefineIterations; i++)
        {
            if (errorC < errorD)
            {
                b = d;
                d = c;
                errorD = errorC;
                c = b - (GoldenRatio * (b - a));
                errorC = Evaluate(n, c, out _, out _, out _);
            }
            else
            {
                a = c;
                c = d;
                errorC = errorD;
                d = a + (GoldenRatio * (b - a));
                errorD = Evaluate(n, d, out _, out _, out _);
            }
        }

        double logRate = (a + b) / 2;
        if (!double.IsFinite(Evaluate(n, logRate, out ambient, out amplitude, out residualSquares)))
        {
            return false;
        }

        rate = Math.Exp(logRate);
        return true;
    }

    /// <summary>
    /// Least-squares <c>A</c> and <c>C</c> for <c>k = e^logRate</c>.
    /// </summary>
    /// <returns>The objective (residual squares plus the prior term); infinity when the system is degenerate.</returns>
    private double Evaluate(int n, double logRate, out double ambient, out double amplitude, out double residualSquares)
    {
        double rate = Math.Exp(logRate);
        double sumX = 0;
        double sumXX = 0;
        double sumY = 0;
        double sumXY = 0;
        for (int i = 0; i < n; i++)
        {
            double x = Math.Exp(-rate * _t[i]);
            _x[i] = x;
            sumX += x;
            sumXX += x * x;
            sumY += _y[i];
            sumXY += x * _y[i];
        }

        double weight = n + AmbientPriorWeight;
        double priorY = sumY + (AmbientPriorWeight * AmbientPriorCelsius);
        double determinant = (weight * sumXX) - (sumX * sumX);
        if (!(determinant > 1e-12 * weight * sumXX))
        {
            ambient = amplitude = residualSquares = 0;
            return double.PositiveInfinity;
        }

        ambient = ((priorY * sumXX) - (sumX * sumXY)) / determinant;
        amplitude = ((weight * sumXY) - (sumX * priorY)) / determinant;
        residualSquares = 0;
        for (int i = 0; i < n; i++)
        {
            double residual = _y[i] - ambient - (amplitude * _x[i]);
            residualSquares += residual * residual;
        }

        double priorOffset = ambient - AmbientPriorCelsius;
        double objective = residualSquares + (AmbientPriorWeight * priorOffset * priorOffset);
        return double.IsFinite(objective) ? objective : double.PositiveInfinity;
    }
}
