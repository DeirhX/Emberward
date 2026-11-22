namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>
/// Emberite nests the player's side can see or read on its radar (<see cref="NestAlert"/>): one waking, and one
/// starving out. No tank or side is named.
/// </summary>
internal sealed class NestWatch : IEventWatch
{
    private bool _explained;
    private readonly List<Position> _woke = new();
    private readonly List<Position> _starved = new();

    public void Reset()
    {
        _explained = false;
        _woke.Clear();
        _starved.Clear();
    }

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Kind == StepEventKind.NestWoke && NestAlert.Covers(world, player, e.Position))
            _woke.Add(e.Position);
        // The last creature of a nest starved (a count of 0): told to whoever has the nest in sight.
        else if (e.Kind == StepEventKind.EmberiteStarved && e.Count == 0 && world.TrackVision(player).IsVisible(e.Position))
            _starved.Add(e.Position);
    }

    public void Update(in GuidanceFrame f)
    {
        // The first time says what emberites are; after that, a short alert for each nest, not repeated for the same
        // one for a while.
        foreach (Position nest in _woke)
        {
            if (!_explained)
            {
                _explained = true;
                f.Feed.Post(f.Now, "nest-woke", "A nest woke: emberites",
                    "They hunt hot tanks and come in waves: the nest flares before each, and the hotter you run the bigger it is. Clear a nest to free its ore.",
                    NotificationKind.Danger, cooldown: 1e9f);
            }
            else
            {
                f.Feed.Post(f.Now, $"nest-woke-{nest.X}-{nest.Y}", "A nest woke nearby", "Emberites are out. Cool down or find cover.",
                    NotificationKind.Danger, cooldown: 20f);
            }
        }
        _woke.Clear();

        foreach (Position nest in _starved)
            f.Feed.Post(f.Now, $"nest-starved-{nest.X}-{nest.Y}", "A nest starved", "No heat was left for its emberites. Its ore is free.",
                NotificationKind.Good, cooldown: 0f);
        _starved.Clear();
    }
}
