namespace Emberward.Protocol;

/// <summary>
/// When a client plays the bundles it has received. Bundles leave the relay evenly but reach a client unevenly (a
/// Wi-Fi link holds a few back and then delivers them together), and a client that simulated each one as it came,
/// or two at once after a late one, would show that unevenness as stutter in every moving thing, the camera
/// included. The pacer keeps a few bundles in hand instead, as many as the arrivals of the last few seconds say are
/// needed (<see cref="TargetDepth"/>), and plays them at the relay's rate, a little faster while more than that are
/// waiting and a little slower while fewer are (<see cref="StepLength"/>), so a backlog drains and a cushion
/// builds without a jump. A bundle that is late anyway is played the moment it arrives (<see cref="StepsDue"/>).
/// </summary>
/// <remarks>
/// On a steady link the target stays at 0, so input delay is what it was without the pacer. Each bundle in hand
/// adds one tick of input delay; the target is capped at <see cref="MaxDepth"/>.
/// </remarks>
public sealed class PlayoutPacer
{
    /// <summary>The most bundles kept in hand, however uneven the arrivals: each one is a tick of input delay.</summary>
    public const int MaxDepth = 4;

    /// <summary>Arrivals remembered for the jitter measure: ten seconds at 24 ticks a second.</summary>
    public const int Window = 240;

    /// <summary>Lateness below this is the client's own frame timing, not the link's, and needs no bundle in hand.</summary>
    public static readonly TimeSpan LatenessSlack = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// An arrival this far from the others is a break in the clock (a pause, a held join, a replay of the past), not
    /// jitter: the measure starts again from it.
    /// </summary>
    public static readonly TimeSpan Discontinuity = TimeSpan.FromSeconds(1);

    /// <summary>The target falls by at most one bundle this often, so a quieter link drains its cushion gently.</summary>
    public static readonly TimeSpan TargetFallInterval = TimeSpan.FromSeconds(1);

    /// <summary>Fastest and slowest a step may be played, as a share of the relay's step.</summary>
    public const double FastestScale = 0.5, SlowestScale = 1.1;

    /// <summary>How much faster per bundle more than wanted; and the error ignored either side of level.</summary>
    private const double Gain = 0.1, DeadZone = 0.5;

    /// <summary>The weight of each step's sample in the smoothed queue length (about ten steps' memory).</summary>
    private const double Smoothing = 0.1;

    private readonly TimeSpan _step;
    private readonly double[] _offsets = new double[Window];
    private int _offsetCount, _offsetNext;
    private int _target;
    private TimeSpan _targetFellAt;
    private double _queued = 1;

    // The clock of a client that has no fixed-step loop of its own (StepsDue).
    private bool _clockRunning;
    private TimeSpan _clockAt, _owed;

    /// <param name="step">The relay's step: one over its ticks a second.</param>
    public PlayoutPacer(TimeSpan step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        _step = step;
    }

    /// <summary>For a match of <paramref name="ticksPerSecond"/>; its step is in whole ticks of time, as the game's is.</summary>
    public static PlayoutPacer ForRate(int ticksPerSecond) =>
        new(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / Math.Max(1, ticksPerSecond)));

    /// <summary>The relay's step.</summary>
    public TimeSpan NominalStep => _step;

    /// <summary>Bundles to keep in hand: enough to ride out the latest arrivals of the last <see cref="Window"/>.</summary>
    public int TargetDepth => _target;

    /// <summary>
    /// The latest arrival in the window behind the earliest, by the relay's clock: how much the link's delay varies.
    /// </summary>
    public TimeSpan Jitter { get; private set; }

    /// <summary>
    /// The bundle for <paramref name="tick"/> arrived at <paramref name="at"/> by the client's clock. Only live ticks
    /// count: the past a returning player is sent all at once says nothing about the link.
    /// </summary>
    public void Arrived(int tick, TimeSpan at)
    {
        double offset = at.TotalMilliseconds - tick * _step.TotalMilliseconds;
        if (_offsetCount > 0)
        {
            (double min, double max) = Spread();
            double limit = Discontinuity.TotalMilliseconds;
            if (offset < max - limit || offset > min + limit)
                Forget();
        }
        _offsets[_offsetNext] = offset;
        _offsetNext = (_offsetNext + 1) % Window;
        _offsetCount = Math.Min(_offsetCount + 1, Window);

        (double earliest, double latest) = Spread();
        Jitter = TimeSpan.FromMilliseconds(latest - earliest);
        double lateness = Math.Max(0, latest - earliest - LatenessSlack.TotalMilliseconds);
        int wanted = Math.Min(MaxDepth, (int)Math.Ceiling(lateness / _step.TotalMilliseconds));
        if (wanted >= _target)
        {
            _target = wanted;
            _targetFellAt = at;
        }
        else if (at - _targetFellAt >= TargetFallInterval)
        {
            _target--;
            _targetFellAt = at;
        }
    }

    /// <summary>
    /// The relay's clock stopped (a pause, a held join): what came before says nothing about what comes after.
    /// The target stays, so a link that was uneven keeps its cushion.
    /// </summary>
    public void Forget()
    {
        _offsetCount = 0;
        _offsetNext = 0;
    }

    private (double Min, double Max) Spread()
    {
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < _offsetCount; i++)
        {
            min = Math.Min(min, _offsets[i]);
            max = Math.Max(max, _offsets[i]);
        }
        return (min, max);
    }

    /// <summary>
    /// How long the next step should take: the relay's step, shortened while more bundles wait than
    /// <see cref="TargetDepth"/> wants, and lengthened while fewer do.
    /// </summary>
    public TimeSpan StepLength()
    {
        // At the moment a step is due the one to be played is waiting too.
        double error = _queued - (_target + 1);
        double scale = Math.Abs(error) <= DeadZone ? 1.0 : Math.Clamp(1.0 - Gain * error, FastestScale, SlowestScale);
        return _step * scale;
    }

    /// <summary>A step was taken with <paramref name="queued"/> bundles waiting, the one played among them.</summary>
    public void Stepped(int queued) => _queued += Smoothing * (queued - _queued);

    /// <summary>
    /// For a client without a fixed-step loop of its own: how many of the <paramref name="queued"/> bundles to play
    /// now, at <paramref name="now"/>, never more than <paramref name="maxSteps"/>. A step due with nothing to play
    /// is owed, and taken as soon as a bundle comes, never two at once to make up for it. Each step counted is
    /// reported to <see cref="Stepped"/>.
    /// </summary>
    public int StepsDue(TimeSpan now, int queued, int maxSteps)
    {
        if (!_clockRunning)
        {
            _clockRunning = true;
            _clockAt = now;
            _owed = StepLength();
        }
        _owed += now - _clockAt;
        _clockAt = now;
        int steps = 0;
        TimeSpan length = StepLength();
        while (steps < maxSteps && steps < queued && _owed >= length)
        {
            _owed -= length;
            Stepped(queued - steps);
            steps++;
            length = StepLength();
        }
        // Starved, or at the cap: one step is owed, not the time that has passed.
        if (_owed > length)
            _owed = length;
        return steps;
    }

    /// <summary>
    /// How far the clock of <see cref="StepsDue"/> has run towards the next step, 0 to 1: where moving things are
    /// drawn between the last step and the next. It holds at 1 while a step is owed.
    /// </summary>
    public float Progress => _clockRunning ? (float)Math.Clamp(_owed / StepLength(), 0.0, 1.0) : 1f;
}
