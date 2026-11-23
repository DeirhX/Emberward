namespace Emberward.Desktop.Gui.Guidance;

using Emberward.Core;
using Emberward.Core.Types;
using Emberward.Core.Entities.Tanks;
using Emberward.Desktop.Gui.Guidance.Tutorial;

/// <summary>What the tutorial asks of the player right now.</summary>
/// <param name="Progress">Optional progress line (e.g. "Dirt 87 / 150") and fill fraction.</param>
/// <param name="Target">Optional world cell the HUD points at.</param>
public readonly record struct TutorialPrompt(
    int Number, int Count, string Title, string Body, string? Progress, float ProgressFraction, Position? Target);

/// <summary>
/// The tutorial: an ordered list of next logical steps, from leaving the base to tapping the core. Each system has its
/// steps in <c>Guidance/Tutorial</c> (<see cref="TutorialStep"/>); this lists them in the order the player meets them.
/// A new system's steps are new step classes and their lines in <see cref="_steps"/>; retiring a system deletes its
/// file and those lines. Each step knows when it is done by reading the world, so a player who does things early or
/// out of order simply skips ahead; nothing is forced. Steps that only explain something finish once the player has
/// seen them for a moment or has met the thing they describe.
/// </summary>
public sealed class TutorialDirector
{
    /// <summary>Title of the step that explains the network circles: the first that may mention being off it.</summary>
    public const string NetworkStepTitle = StayOnNetworkStep.StepTitle;

    /// <summary>Title of the step that opens the base upgrades.</summary>
    public const string UpgradeStepTitle = BaseUpgradeStep.StepTitle;

    /// <summary>
    /// Seconds a step stays up once the player has done it, so a fast player still sees what each step says. A step
    /// that is already done when it comes up is skipped at once: nothing is held back from a player who is ahead.
    /// </summary>
    private const float MinStepSeconds = 4f;

    private readonly TutorialStep[] _steps =
    [
        new LeaveBaseStep(),
        new MineOreStep(),
        new TemperatureStep(),
        new BuildRelayStep(),
        new StayOnNetworkStep(),
        new HotRockPowerStep(),
        new BuildHarvesterStep(),
        new CoolHarvesterStep(),
        new PowerHarvesterStep(),
        new BaseUpgradeStep(),
        new RelayUpgradeStep(),
        new TurretStep(),
        new GatlingStep(),
        new FrostStep(),
        new TapCoreStep(),
    ];

    private int _index;
    private float _shownSince = float.NaN;
    private bool _doneOnArrival;

    public bool IsComplete => _index >= _steps.Length;

    /// <summary>True once the tutorial has come to the step titled <paramref name="title"/>, or is over.</summary>
    public bool HasReached(string title) => _index >= Array.FindIndex(_steps, s => s.Title == title);

    /// <summary>Seconds the current step has been shown.</summary>
    private float ShownFor(float now) => float.IsNaN(_shownSince) ? 0f : now - _shownSince;

    /// <summary>Advances past every step that is done and returns the step to show, or null when the tutorial is over.</summary>
    public TutorialPrompt? Update(World world, Tank player, float now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        if (player.Base != null && !player.IsDead)
            foreach (TutorialStep step in _steps)
                step.Observe(world, player);

        while (_index < _steps.Length)
        {
            var step = _steps[_index];
            bool stateDone = step.IsDone(world, player);
            if (float.IsNaN(_shownSince))
            {
                _shownSince = now;
                _doneOnArrival = stateDone;
            }
            float read = step.ReadSeconds(world);
            bool done = (stateDone && (_doneOnArrival || ShownFor(now) >= MinStepSeconds)) ||
                        (read > 0f && ShownFor(now) >= read);
            if (!done)
                break;
            _index++;
            _shownSince = float.NaN;
        }

        if (IsComplete)
            return null;
        var current = _steps[_index];
        var progress = current.Progress(world, player);
        return new TutorialPrompt(_index + 1, _steps.Length, current.Title, current.Body(world),
            progress?.Text, progress?.Fraction ?? 0f, current.Target(world, player));
    }

    /// <summary>Skips the rest of the tutorial.</summary>
    public void Skip() => _index = _steps.Length;
}
