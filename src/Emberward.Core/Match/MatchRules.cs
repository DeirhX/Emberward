namespace Emberward.Core.Match;

using Emberward.Core.Snapshots;
using Emberward.Core.Config;
using Emberward.Core.Types;

/// <summary>
/// Which of the game's optional systems a world runs with, fixed when the world is built. This is the one place a
/// system is switched on or off for a match: <see cref="World"/> reads it, and a system whose flag is off costs
/// nothing. A system that can be switched off gets a flag here, not a constructor argument or a
/// property set after construction.
/// </summary>
/// <remarks>
/// A new <see cref="MatchRules"/> has every optional system off except regrowth, which is what most tests want;
/// <see cref="Standard"/> is a normal match. The rules are part of <see cref="WorldStateHash.ComputeReady"/>, so two
/// machines that would build the same map under different rules refuse to start a network match together.
/// </remarks>
[NotSaved("fixed for the match: the world is built with the same rules")]
public sealed record MatchRules
{
    /// <summary>Dirt grows back into open ground (<see cref="Regrowth.TerrainRegrowth"/>).</summary>
    public bool TerrainRegrowth { get; init; } = true;

    /// <summary>
    /// How many hot rock pockets level setup places. With at least one there is also a hot core at the map centre,
    /// and so a sector goal (<see cref="SectorGoal"/>).
    /// </summary>
    public int HotRockPockets
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(HotRockPockets), value, "Cannot be negative.");
    }

    /// <summary>How many frost pockets level setup places.</summary>
    public int FrostRockPockets
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(FrostRockPockets), value, "Cannot be negative.");
    }

    /// <summary>Level setup seeds pyrite deposits (<see cref="LevelGen.OreDeposits"/>).</summary>
    public bool SeedOre { get; init; }

    /// <summary>
    /// The first base to tap the core wins the match (a race, DESIGN.md §4.8). Off, the core is a goal that does not
    /// end the match. Has no effect without hot rock, since there is then no core.
    /// </summary>
    public bool CoreTapWins { get; init; }

    /// <summary>
    /// Every machine has a capacitor and islands with a base or generator feed their machines by priority tier (the energy network,
    /// DESIGN.md §4.9, <see cref="Network.EnergyNetwork"/>). Off, no machine has a capacitor and a harvester pays for
    /// its work straight from its base's store (§4.5).
    /// </summary>
    public bool EnergyNetwork { get; init; }

    /// <summary>
    /// A tank can buy upgrades for its own machines, next to them, from its base's ore store (DESIGN.md §4.9,
    /// <see cref="Entities.Machines.MachineUpgradeSystem"/>). Off, machines keep their base numbers.
    /// </summary>
    public bool MachineUpgrades { get; init; }

    /// <summary>
    /// The off-network clock is the tank's reserve (DESIGN.md §4.9, <see cref="Entities.Tanks.TankReserveSystem"/>):
    /// the recall key burns carried pyrite to buy it back, and running out goes through warning, degraded and
    /// stranded before heat death. Off, the clock runs out straight into overheating.
    /// </summary>
    public bool TankReserve { get; init; }

    /// <summary>
    /// Emberites nest in pyrite pockets and lumps, guard the ore and hunt tanks that run hot (DESIGN.md §6.2,
    /// <see cref="Entities.Emberites.EmberiteSwarm"/>). Needs <see cref="SeedOre"/>: without deposits there are no nests. Off, the
    /// world has no swarm and no harvester refuses ore.
    /// </summary>
    public bool Emberites { get; init; }

    /// <summary>
    /// The tank's life is a regenerating shield in front of a hull (DESIGN.md §4.7, <see cref="Entities.Tanks.TankShieldSystem"/>):
    /// damage takes the shield first, the shield refills after a short delay, and the hull is repaired only at the base.
    /// Off, the tank has the hull alone and every hit takes it.
    /// </summary>
    public bool TankShield { get; init; }

    /// <summary>
    /// The match has a clock of this many seconds and is won on kills (<see cref="MatchScore"/>, DESIGN.md §6.8): when
    /// the clock runs out the tank that leads alone wins, and while the top score is tied the match goes on until one
    /// does. 0, the default, is no clock and no score.
    /// </summary>
    public int TimeLimitSeconds
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(TimeLimitSeconds), value, "Cannot be negative.");
    }

    /// <summary>
    /// No base can be destroyed, so no side is ever out of the match: a shell that reaches a core bursts on it as on a
    /// wall. The gate shield and the gates work as they do without the rule. Off, a base falls when its core does.
    /// </summary>
    public bool BasesHold { get; init; }

    /// <summary>
    /// A dead tank is back after <see cref="Tweaks.Arcade.RespawnSeconds"/> whatever its base's charge. Off, an energy
    /// match makes it wait longer the emptier the base's store is.
    /// </summary>
    public bool QuickRespawn { get; init; }

    /// <summary>
    /// Powerup drops land on a fixed, seeded schedule, each announced a full period ahead to every side
    /// (DESIGN.md §6.8, <see cref="Entities.Powerups.PowerupDrops"/>). Off, the world has no drops.
    /// </summary>
    public bool Powerups { get; init; }

    /// <summary>A normal match: hot and frost pockets, ore, regrowth, and the energy rules (the energy network with its gate
    /// shields, machine upgrades and the tank's reserve); the core does not end it.
    /// </summary>
    public static MatchRules Standard { get; } = new()
    {
        HotRockPockets = Tweaks.World.HotRockPocketCount,
        FrostRockPockets = Tweaks.World.FrostRockPocketCount,
        SeedOre = true,
        EnergyNetwork = true,
        MachineUpgrades = true,
        TankReserve = true,
        TankShield = true,
    };

    /// <summary>
    /// An arcade match (DESIGN.md §6.8): a standard match with emberites that runs <see cref="Tweaks.Arcade.TimeLimitSeconds"/>
    /// and is won on kills, where bases hold, dead tanks are back quickly, powerups drop and the core does not end the
    /// match.
    /// </summary>
    public static MatchRules Arcade { get; } = Standard with
    {
        Emberites = true,
        CoreTapWins = false,
        TimeLimitSeconds = Tweaks.Arcade.TimeLimitSeconds,
        BasesHold = true,
        QuickRespawn = true,
        Powerups = true,
    };

    /// <summary>
    /// The rules of a match of <paramref name="mode"/>, for every host alike. A standard match has emberites and ends
    /// on the core when <paramref name="coreTapWins"/>; an arcade match is <see cref="Arcade"/>, whose clock ends it
    /// (<paramref name="coreTapWins"/> is ignored).
    /// </summary>
    public static MatchRules ForMode(MatchMode mode, bool coreTapWins) => mode switch
    {
        MatchMode.Standard => Standard with { CoreTapWins = coreTapWins, Emberites = true },
        MatchMode.Arcade => Arcade,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a match mode."),
    };

    /// <summary>The rules as one number, the same on every machine and run (unlike <see cref="GetHashCode"/>).</summary>
    public ulong Fingerprint()
    {
        ulong hash = Fnv1a.Offset;
        hash = Fnv1a.Mix(hash, TerrainRegrowth ? 1u : 0u);
        hash = Fnv1a.Mix(hash, (uint)HotRockPockets);
        hash = Fnv1a.Mix(hash, (uint)FrostRockPockets);
        hash = Fnv1a.Mix(hash, SeedOre ? 1u : 0u);
        hash = Fnv1a.Mix(hash, CoreTapWins ? 1u : 0u);
        // Version of the base rules: a base falls when its core does, behind walls and gates (the keep).
        hash = Fnv1a.Mix(hash, 9u);
        // Version of the energy rules: raised with each machine they add (turrets, the gatling) and with the gate shield.
        if (EnergyNetwork)
            hash = Fnv1a.Mix(hash, 0xE4ECu);
        if (MachineUpgrades)
            hash = Fnv1a.Mix(hash, 0x4D55u);
        if (TankReserve)
            hash = Fnv1a.Mix(hash, 0x5253u);
        if (TankShield)
            hash = Fnv1a.Mix(hash, 0x5348u);
        // Version of the emberite rules: raised when what creatures do changes (a dying one drops its nest's ore).
        if (Emberites)
            hash = Fnv1a.Mix(hash, 0x4D50u);
        // The arcade rules, each mixed only when it is on, so the rules of a match without them hash as they always did.
        if (TimeLimitSeconds > 0)
            hash = Fnv1a.Mix(Fnv1a.Mix(hash, 0x544Cu), (uint)TimeLimitSeconds);
        if (BasesHold)
            hash = Fnv1a.Mix(hash, 0x4248u);
        if (QuickRespawn)
            hash = Fnv1a.Mix(hash, 0x5152u);
        // Version of the powerup rules: raised when the schedule, the sites, the kinds or what a kind does changes
        // (last: a drop lies only until the next lands, and the contested sites' turns go in shuffled rounds).
        if (Powerups)
            hash = Fnv1a.Mix(hash, 0x5056u);
        return hash;
    }
}
