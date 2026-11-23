using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;
using Emberward.Desktop.Gui.Guidance;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>Another tank's death that no side is credited with is told (<c>TankDeathWatch</c>); a kill, the player's own death and a recall are not.</summary>
public class TankDeathWatchTests
{
    private static (World World, GameEventWatcher Events, NotificationFeed Feed) Start()
    {
        var world = Create(ArcadeWorlds.Timed(480));
        return (world, new GameEventWatcher(), new NotificationFeed());
    }

    private static void Hear(World w, GameEventWatcher events, NotificationFeed feed)
    {
        Tank player = w.TankList.Tanks[0];
        events.AfterStep(w, player);
        events.Update(w, player, feed, w.AdvanceCount / 60f);
    }

    [Fact]
    public void AnotherTanksOverheat_IsToasted_ButThePlayersOwnDeathAndARecallAreNot()
    {
        var (w, events, feed) = Start();
        Hear(w, events, feed);

        Perish(w, w.TankList.Tanks[1], DamageCause.Overheat);
        Hear(w, events, feed);
        var toast = Assert.Single(feed.Active, n => n.Key == "tank-died-other");
        Assert.Equal("It overheated.", toast.Detail);

        var (w2, events2, feed2) = Start();
        Hear(w2, events2, feed2);
        Perish(w2, w2.TankList.Tanks[0], DamageCause.Overheat);
        Hear(w2, events2, feed2);
        Assert.DoesNotContain(feed2.Active, n => n.Key == "tank-died-other");

        var (w3, events3, feed3) = Start();
        Hear(w3, events3, feed3);
        Perish(w3, w3.TankList.Tanks[1], DamageCause.Recalled);
        Hear(w3, events3, feed3);
        Assert.DoesNotContain(feed3.Active, n => n.Key == "tank-died-other");
    }
}
