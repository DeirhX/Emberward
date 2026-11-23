namespace Emberward.Desktop.Gui;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Desktop.Presentation;

/// <summary>One death on the scoreboard: when in the match, and how (with who killed the tank, if anyone did).</summary>
public readonly record struct DeathMark(string Clock, string Cause);

/// <summary>A tank's row on the scoreboard: its colour name, how many tanks it (or its machines) killed, and its deaths, oldest first.</summary>
public sealed record ScoreRow(int Tank, string Name, int Kills, IReadOnlyList<DeathMark> Deaths);

/// <summary>
/// What the HUD says of tank deaths, whichever host draws it: a tank's colour name, who killed whom and how, the match
/// clock, and the scoreboard rows the Tab key shows (from <see cref="World.Deaths"/>).
/// </summary>
public static class TankDeathView
{
    // In the order of TankSprites.TankColors.
    private static readonly string[] ColorNames = ["Blue", "Green", "Red", "Pink", "Purple", "White", "Aqua", "Gray"];

    /// <summary>The tank's colour as a word ("Red"), by its colour index.</summary>
    public static string ColorName(int tank) =>
        (uint)tank < (uint)ColorNames.Length ? ColorNames[tank] : $"Tank {tank + 1}";

    /// <summary>The killer as the subject of a sentence: "Blue", "Blue's turret", or "You" / "Your turret" for the player.</summary>
    public static string KillerName(Killer by, int player)
    {
        bool mine = by.Color == player;
        if (by.Machine is not { } machine)
            return mine ? "You" : ColorName(by.Color);
        string name = MachinePresentations.Get(machine).Name;
        return mine ? $"Your {name}" : $"{ColorName(by.Color)}'s {name}";
    }

    /// <summary>How a tank died, in a few words for the scoreboard ("shot by Red", "Red's turret", "overheated").</summary>
    public static string CauseWord(DamageCause cause, Killer? by = null) => (cause, by) switch
    {
        (DamageCause.EnemyFire, { Machine: { } machine } killer) =>
            $"{ColorName(killer.Color)}'s {MachinePresentations.Get(machine).Name}",
        (DamageCause.EnemyFire, { } killer) => $"shot by {ColorName(killer.Color)}",
        (DamageCause.EnemyFire, _) => "shot",
        (DamageCause.Overheat, _) => "overheated",
        (DamageCause.Frostbite, _) => "froze",
        (DamageCause.BaseDestroyed, _) => "base fell",
        (DamageCause.Recalled, _) => "recalled",
        (DamageCause.Emberite, _) => "emberites",
        _ => "destroyed",
    };

    /// <summary>
    /// The toast for another tank's death, as it happens: who killed it in the title when a side did ("You destroyed
    /// the Red tank", "Green's turret destroyed the Red tank"), else the tank and the cause.
    /// </summary>
    public static (string Title, string Detail) DeathToast(int tank, DamageCause cause, Killer? by, int player)
    {
        string victim = ColorName(tank);
        if (by is { } killer)
            return ($"{KillerName(killer, player)} destroyed the {victim} tank", "It is out until it respawns.");
        return ($"{victim} tank destroyed", cause switch
        {
            DamageCause.EnemyFire => "Shot down.",
            DamageCause.Overheat => "It overheated.",
            DamageCause.Frostbite => "It froze.",
            DamageCause.BaseDestroyed => "Its base fell with it.",
            DamageCause.Emberite => "Emberites got it.",
            _ => "It is out until it respawns.",
        });
    }

    /// <summary>The death screen's line for the player's own tank: what destroyed it, and who.</summary>
    public static string DeathLine(Tank player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return (player.DeathCause, player.DeathBy) switch
        {
            (DamageCause.EnemyFire, { } by) => $"Destroyed by {KillerName(by, player.Color)}",
            (DamageCause.EnemyFire, _) => "Destroyed by enemy fire",
            (DamageCause.Overheat, _) => "Destroyed by overheating",
            (DamageCause.Frostbite, _) => "Destroyed by frostbite",
            (DamageCause.Emberite, _) => "Destroyed by emberites",
            _ => "Destroyed",
        };
    }

    /// <summary>A match time as minutes and seconds ("3:07").</summary>
    public static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    /// <summary>Every tank, in list order, with its kills and the times it died.</summary>
    public static IReadOnlyList<ScoreRow> Rows(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        int count = world.TankList.Count;
        var deaths = new List<DeathMark>[count];
        var kills = new int[count];
        for (int i = 0; i < count; i++)
            deaths[i] = new List<DeathMark>();
        foreach (TankDeath death in world.Deaths)
        {
            if ((uint)death.Tank < (uint)count)
                deaths[death.Tank].Add(new DeathMark(Clock(world.MatchTimeAt(death.Step)), CauseWord(death.Cause, death.By)));
            if (death.By is { } by && (uint)by.Color < (uint)count && by.Color != death.Tank)
                kills[by.Color]++;
        }

        var rows = new ScoreRow[count];
        for (int i = 0; i < count; i++)
            rows[i] = new ScoreRow(i, ColorName(world.TankList.Tanks[i].Color), kills[i], deaths[i]);
        return rows;
    }
}
