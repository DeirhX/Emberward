namespace Emberward.Core.Config;

public static partial class Tweaks
{
    public static partial class Bot
    {
        // Prospecting (BotProspector, DESIGN.md §6.4). In a match with ore, a bot spends its outings mining instead of
        // heading for its enemy: it digs toward the nearest pocket or speck within ProspectRadiusCells of its own base,
        // fills its hold and takes the load home to the core, as a player does. With no ore left in its area it roams
        // there. Lumps are left alone: their core is hot enough to cook the tank.
        public const int ProspectRadiusCells = 100;
        // It prospects for this long from the start of the match; after that it hunts as before.
        public const int ProspectSeconds = 240;
        // It fights instead while its enemy is within EngageRangeCells (along either axis), in sight, at its own base,
        // or has hit it; and for EngageHoldSeconds after the last of those, so an enemy at the edge of the range does
        // not flip it between mining and fighting every tick.
        public const int EngageRangeCells = 45;
        // In sight counts only within this many cells (along either axis): the bot's sight passes through dirt, so a
        // far enemy across a dirt field would otherwise end every outing.
        public const int EngageSightCells = 75;
        public const int EngageHoldSeconds = 8;
        // How far round a deposit's start cell its ore is looked for (a 30-cell pocket stays well inside).
        public const int DepositReachCells = 12;
        // Within this many cells (along either axis) of the ore cell it is after, it drives straight at it and digs.
        public const int MineReachCells = 9;
        // Digging without moving or gaining ore for MineTorchSeconds (rock in the way), it torches while its heat
        // allows; for MineGiveUpSeconds, it leaves that deposit alone for SkipDepositSeconds.
        public const int MineTorchSeconds = 2;
        public const int MineGiveUpSeconds = 6;
        public const int SkipDepositSeconds = 60;
        // With no deposit to go to it looks again this often, and heads for a new place in its area when it reaches
        // the last one or after RoamPointSeconds.
        public const int ProspectSearchSeconds = 3;
        public const int RoamPointSeconds = 15;
    }
}
