namespace Emberward.Desktop;

public sealed class GameLoopScheduler
{
    private TimeSpan _step;
    private readonly int _maxCatchUpSteps;
    private TimeSpan _accumulator;

    public GameLoopScheduler(TimeSpan step, int maxCatchUpSteps)
    {
        _step = step;
        _maxCatchUpSteps = Math.Max(1, maxCatchUpSteps);
    }

    public bool HasReadyStep => _accumulator >= _step;

    /// <summary>
    /// The time a step takes. A network match changes it from frame to frame to play the relay's bundles evenly
    /// (<see cref="Emberward.Protocol.PlayoutPacer"/>).
    /// </summary>
    public TimeSpan Step
    {
        get => _step;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _step = value;
        }
    }

    /// <summary>
    /// A step is due but cannot be taken yet (its network bundle has not arrived): it stays owed, to be taken the
    /// moment it can, and the time beyond it is dropped, so a late step is never followed by a second at once.
    /// Displayed frames hold at the latest step meanwhile.
    /// </summary>
    public void Stall()
    {
        if (_accumulator > _step)
            _accumulator = _step;
    }

    /// <summary>Time left until the next simulation step is due (zero or negative when one is ready).</summary>
    public TimeSpan TimeUntilNextStep => _step - _accumulator;

    /// <summary>
    /// How far the time since the last consumed step has run towards the next one: 0 right after a step, 1 when
    /// the next is due. Displayed frames use it to place moving things between the two.
    /// </summary>
    public float StepProgress => (float)Math.Clamp(_accumulator.Ticks / (double)_step.Ticks, 0.0, 1.0);

    /// <summary>Drops the steps that are due without running them, so a paused game does not catch up on resume.</summary>
    public void DiscardReadySteps()
    {
        if (_accumulator >= _step)
            _accumulator = TimeSpan.FromTicks(_accumulator.Ticks % _step.Ticks);
    }

    public void AddElapsed(TimeSpan elapsed)
    {
        _accumulator += elapsed;
    }

    public int ConsumeReadySteps(Action onStep, Func<bool> canContinue)
    {
        int executed = 0;
        while (_accumulator >= _step && executed < _maxCatchUpSteps && canContinue())
        {
            _accumulator -= _step;
            executed++;
            onStep();
        }

        if (executed == _maxCatchUpSteps && _accumulator > _step)
            _accumulator = _step;

        return executed;
    }
}
