namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;

/// <summary>The machine network: relays linking and losing their link, harvesters at work, the tank leaving it.</summary>
internal sealed class NetworkWatch : IEventWatch
{
    private Tally _poweredRelays;
    private Tally _harvesters;
    private Tally _stalledHarvesters;
    private Tally _dryHarvesters;
    private Latch _offNetwork;

    public void Reset() { }

    public void OnStepEvent(World world, Tank player, in StepEvent e) { }

    public void Update(in GuidanceFrame f)
    {
        int relays = _poweredRelays.Change(f.Network.PoweredRelays);
        int planted = _harvesters.Change(f.Network.Harvesters);
        int stalled = _stalledHarvesters.Change(f.Network.HarvestersTooHot);
        int dry = _dryHarvesters.Change(f.Network.HarvestersOutOfCharge);
        // Recorded while the tutorial keeps quiet, so leaving the network is not told late once it has spoken.
        bool leftNetwork = _offNetwork.Rose(f.Player.IsOffNetwork);
        if (!f.Primed)
            return;

        if (relays > 0)
            f.Feed.Post(f.Now, "relay-linked", "Relay linked",
                "It pumps heat home whenever the ground around it is warm, and widens your network.", NotificationKind.Good, cooldown: 4f);
        else if (relays < 0)
            f.Feed.Post(f.Now, "relay-lost", "Relay lost its link",
                "Something blocks its line to the network. Dig the line clear or plant a relay closer.", NotificationKind.Warning);

        if (planted > 0)
            f.Feed.Post(f.Now, "harvester-planted", "Harvester working",
                "Inside your network it mines the ore around it into your base store. Keep it cool and charged.", NotificationKind.Good, cooldown: 4f);
        if (stalled > 0)
            f.Feed.Post(f.Now, "harvester-stalled", "Harvester stalled: too hot",
                "Mining heats the ground. A linked relay near it pumps that heat away.", NotificationKind.Warning);
        if (dry > 0)
            f.Feed.Post(f.Now, "harvester-dry", "Harvester out of charge",
                "It runs on your base's charge where your network covers it. Put a relay by hot rock to make more.",
                NotificationKind.Danger);

        if (leftNetwork && !f.IntroQuiet)
            f.Feed.Post(f.Now, "off-network", "Off network",
                "Your tank heats up out here. Head back inside the pale circles, or extend the network with a relay.",
                NotificationKind.Warning, cooldown: 90f);
    }
}
