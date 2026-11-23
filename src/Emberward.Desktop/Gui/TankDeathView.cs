namespace Emberward.Desktop.Gui;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Match;

/// <summary>One death on the scoreboard: when in the match, and why.</summary>
public readonly record struct DeathMark(string Clock, string Cause);

/// <summary>A tank's row on the scoreboard: its colour name and its deaths, oldest first.</summary>
public sealed record ScoreRow(int Tank, string Name, IReadOnlyList<DeathMark> Deaths);

/// <summary>
/// What the HUD says of tank deaths, whichever host draws it: a tank's colour name, a death's cause in a word, the match
/// clock, and the scoreboard rows the Tab key shows (from <see cref="World.Deaths"/>).
/// </summary>
public static class TankDeathView
{
    // In the order of TankSprites.TankColors.
    private static readonly string[] ColorNames = ["Blue", "Green", "Red", "Pink", "Purple", "White", "Aqua", "Gray"];

    /// <summary>The tank's colour as a word ("Red"), by its colour index.</summary>
    public static string ColorName(int tank) =>
        (uint)tank < (uint)ColorNames.Length ? ColorNames[tank] : $"Tank {tank + 1}";

    /// <summary>Why a tank died, in a word or two for the scoreboard.</summary>
    public static string CauseWord(DamageCause cause) => cause switch
    {
        DamageCause.EnemyFire => "shot",
        DamageCause.Overheat => "overheated",
        DamageCause.Frostbite => "froze",
        DamageCause.BaseDestroyed => "base fell",
        DamageCause.Recalled => "recalled",
        DamageCause.Emberite => "emberites",
        _ => "destroyed",
    };

    /// <summary>Why an enemy tank died, as the line under its toast.</summary>
    public static string CauseLine(DamageCause cause) => cause switch
    {
        DamageCause.EnemyFire => "Shot down.",
        DamageCause.Overheat => "It overheated.",
        DamageCause.Frostbite => "It froze.",
        DamageCause.BaseDestroyed => "Its base fell with it.",
        DamageCause.Emberite => "Emberites got it.",
        _ => "It is out until it respawns.",
    };

    /// <summary>A match time as minutes and seconds ("3:07").</summary>
    public static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    /// <summary>Every tank, in list order, with the times it died.</summary>
    public static IReadOnlyList<ScoreRow> Rows(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        int count = world.TankList.Count;
        var deaths = new List<DeathMark>[count];
        for (int i = 0; i < count; i++)
            deaths[i] = new List<DeathMark>();
        foreach (TankDeath death in world.Deaths)
            if ((uint)death.Tank < (uint)count)
                deaths[death.Tank].Add(new DeathMark(Clock(world.MatchTimeAt(death.Step)), CauseWord(death.Cause)));

        var rows = new ScoreRow[count];
        for (int i = 0; i < count; i++)
            rows[i] = new ScoreRow(i, ColorName(world.TankList.Tanks[i].Color), deaths[i]);
        return rows;
    }
}
