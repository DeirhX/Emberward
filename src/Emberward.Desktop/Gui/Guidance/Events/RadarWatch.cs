namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;

/// <summary>The tank's radar: hot ground scattering its pulse.</summary>
internal sealed class RadarWatch : IEventWatch
{
    private Latch _scattered;

    public void Reset() { }

    public void OnStepEvent(World world, Tank player, in StepEvent e) { }

    public void Update(in GuidanceFrame f)
    {
        bool scattered = !f.Player.IsDead &&
            f.World.RadarOf(f.Player) is { HeatLoss: >= GameEventWatcher.RadarScatteredShare };
        // Recorded while the tutorial keeps quiet, like leaving the network.
        if (_scattered.Rose(scattered) && f.Primed && !f.IntroQuiet)
            f.Feed.Post(f.Now, "radar-heat", "Radar scattered by heat",
                "Hot ground swallows the pulse: red dots show how far it got. Cool that ground to see past it.",
                NotificationKind.Warning, cooldown: 60f);
    }
}
