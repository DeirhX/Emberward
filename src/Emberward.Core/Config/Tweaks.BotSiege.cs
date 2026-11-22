namespace Emberward.Core.Config;

public static partial class Tweaks
{
    public static partial class Bot
    {
        // The longest a bot reckons it can keep firing at a base in one outing (World.CanBreakBase); its off-network
        // clock may cut it shorter. A fresh tank fires 76,800 damage in it, which falls short of a sound keep with its
        // store behind it: at 1,280 a second against a shield that buys back 480 a second, the 60,000 shield alone holds
        // 75 seconds. A lone bot lays siege to a worn, dark or unshielded base and waits outside a sound one.
        public const int SiegeWindowSeconds = 60;
        // Once a bot has decided it can break a base, it keeps to that while its fire, counted this many times as
        // strong, still would: a store whose charge hovers at the edge does not flip it between siege and waiting
        // every tick. It decides afresh when its enemy's base changes.
        public const double SiegeHoldFireFactor = 1.15;
        // While its enemy sits docked in a base it cannot break, the bot waits this many cells out from the wall, beside
        // the gate nearest it (out of the gate's line of fire), and holds its fire (BotLoiter).
        public const int LoiterCellsOutsideWall = 12;
        // A bot on its way to its post that gets no nearer it for this long gives up waiting there for a while and roams
        // (where the stuck watch sees it) instead of pushing at whatever is in its way.
        public const int LoiterApproachSeconds = 15;
    }
}
