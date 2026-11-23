namespace Emberward.Core.Match;

using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>
/// The score and the clock of a timed match (<see cref="MatchRules.TimeLimitSeconds"/>, DESIGN.md §6.8): each tank's
/// kills and deaths, and the ticks played. A tank's score is its kills. When the clock runs out the tank that leads
/// alone has won (<see cref="MatchWinReason.TopScore"/>); while the top score is tied the match goes on, in sudden
/// death, until one tank leads alone. A world without the rule has no score and pays nothing for it.
/// </summary>
/// <remarks>
/// Kills and deaths are read off the step's own <see cref="StepEventKind.TankDied"/> events, after the tanks have
/// advanced, so a step costs one pass over its events and, once the clock has run out, one over the tanks. The tallies
/// are kept here, by the tank's colour, not on the tank. A side whose base has fallen cannot win on score.
/// </remarks>
public sealed class MatchScore
{
    // Walked by index: a foreach over the interface would allocate an enumerator each time the HUD asks.
    private readonly IReadOnlyList<Tank> _tanks;
    private readonly StepEventLog _events;
    private readonly int _limitTicks;

    // Each tank's tally, by its colour (its spawn index); grown to the tanks there are.
    private readonly List<Tally> _tallies = new();
    private int _ticks;

    // The colour of the tank that has won on score; -1 until one has.
    private int _winner = -1;

    /// <param name="tanks">The match's tanks, a live list.</param>
    /// <param name="events">The step's events, which say who died and whose kill it was.</param>
    public MatchScore(IReadOnlyList<Tank> tanks, StepEventLog events, int timeLimitSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeLimitSeconds);
        _tanks = tanks;
        _events = events;
        _limitTicks = timeLimitSeconds * Tweaks.Perf.TargetFps;
    }

    /// <summary>
    /// One step, after the tanks have advanced and before the result is latched: counts the step's deaths, runs the
    /// clock, and once it has run out names the winner as soon as one tank leads alone. Call it only while the match is
    /// undecided, so the board and the clock stand as they were when it ended.
    /// </summary>
    public void Advance()
    {
        if (_winner >= 0)
            return;
        foreach (ref readonly StepEvent e in _events.Events)
        {
            if (e.Kind != StepEventKind.TankDied || e.Color < 0)
                continue;
            Tally victim = TallyOf(e.Color);
            victim.Deaths++;
            _tallies[e.Color] = victim;
            if (e.Killer == e.Color || TankOfColor(e.Killer) == null)
                continue;
            Tally killer = TallyOf(e.Killer);
            killer.Kills++;
            _tallies[e.Killer] = killer;
        }
        _ticks++;
        if (_ticks >= _limitTicks && SoleLeader() is { } leader)
            _winner = leader.Color;
    }

    /// <summary>The tank that has won on score, or null: the clock has not run out, or the top score is still tied.</summary>
    public Tank? Winner => _winner >= 0 ? TankOfColor(_winner) : null;

    /// <summary>
    /// The tank with the highest score among the sides still in the match, when exactly one has it; null while the top
    /// score is shared (as it is before the first kill).
    /// </summary>
    public Tank? SoleLeader()
    {
        Tank? leader = null;
        int best = -1;
        bool shared = false;
        for (int i = 0; i < _tanks.Count; i++)
        {
            Tank tank = _tanks[i];
            if (MatchReferee.IsEliminated(tank))
                continue;
            int score = PeekTally(tank.Color).Kills;
            if (score > best)
                (leader, best, shared) = (tank, score, false);
            else if (score == best)
                shared = true;
        }
        return shared ? null : leader;
    }

    /// <summary>The clock as the HUD reads it.</summary>
    public MatchClock Clock => new(true, _ticks, _limitTicks, IsSuddenDeath: _ticks >= _limitTicks && _winner < 0);

    /// <summary><paramref name="tank"/>'s line on the board: its kills, deaths and place.</summary>
    public TankScore ScoreOf(Tank tank) => Ranked(Line(tank));

    private TankScore Line(Tank tank)
    {
        Tally tally = PeekTally(tank.Color);
        return new TankScore(true, tank.Color, tally.Kills, tally.Deaths, Rank: 0);
    }

    private TankScore Ranked(TankScore line)
    {
        int rank = 1;
        for (int i = 0; i < _tanks.Count; i++)
            if (PeekTally(_tanks[i].Color).Kills > line.Kills)
                rank++;
        return line with { Rank = rank };
    }

    /// <summary>
    /// Writes the board in rank order (<see cref="TankScore"/>): most kills first, then fewest deaths, then lowest
    /// colour. Returns how many lines it wrote, at most <paramref name="destination"/>'s length. Allocates nothing.
    /// </summary>
    public int CopyStandings(Span<TankScore> destination)
    {
        int count = Math.Min(destination.Length, _tanks.Count);
        for (int place = 0; place < count; place++)
        {
            // The best line that comes after the one written before it: the order has no ties, so that is the next.
            TankScore? next = null;
            for (int i = 0; i < _tanks.Count; i++)
            {
                TankScore line = Line(_tanks[i]);
                if ((place == 0 || Precedes(destination[place - 1], line)) && (next is not { } best || Precedes(line, best)))
                    next = line;
            }
            if (next is not { } found)
                return place;
            destination[place] = Ranked(found);
        }
        return count;
    }

    private static bool Precedes(in TankScore a, in TankScore b) =>
        a.Kills != b.Kills ? a.Kills > b.Kills
        : a.Deaths != b.Deaths ? a.Deaths < b.Deaths
        : a.Color < b.Color;

    /// <summary>Mixes the clock, the winner and every tally, for <see cref="WorldStateHash.Compute"/>.</summary>
    internal ulong MixState(ulong hash)
    {
        hash = Fnv1a.Mix(Fnv1a.Mix(hash, _ticks), _winner);
        hash = Fnv1a.Mix(hash, _tallies.Count);
        foreach (Tally tally in _tallies)
            hash = Fnv1a.Mix(Fnv1a.Mix(hash, tally.Kills), tally.Deaths);
        return hash;
    }

    private Tank? TankOfColor(int color)
    {
        for (int i = 0; i < _tanks.Count; i++)
            if (_tanks[i].Color == color)
                return _tanks[i];
        return null;
    }

    private Tally PeekTally(int color) => (uint)color < (uint)_tallies.Count ? _tallies[color] : default;

    private Tally TallyOf(int color)
    {
        while (_tallies.Count <= color)
            _tallies.Add(default);
        return _tallies[color];
    }

    /// <summary>One tank's kills and deaths.</summary>
    private struct Tally
    {
        public int Kills;
        public int Deaths;
    }
}

/// <summary>
/// One tank's line on the board of a timed match (<see cref="World.ScoreOf"/>, <see cref="World.CopyStandings"/>).
/// <see cref="Active"/> is false (and the rest zero) when the match rules run no score.
/// </summary>
/// <param name="Color">The tank's colour (its slot).</param>
/// <param name="Kills">Tanks it destroyed with its own shells or its machines', or last shot before something else finished them (<see cref="StepEvent.Killer"/>).</param>
/// <param name="Deaths">Times it was destroyed, by anything.</param>
/// <param name="Rank">Its place, from 1; tanks with the same score share one.</param>
public readonly record struct TankScore(bool Active, int Color, int Kills, int Deaths, int Rank)
{
    /// <summary>What the match is won on: the kills.</summary>
    public int Score => Kills;
}

/// <summary>
/// The clock of a timed match (<see cref="World.MatchClock"/>), in ticks of <see cref="Tweaks.Perf.TargetFps"/> a
/// second. <see cref="Active"/> is false (and the rest zero) when the match rules set no time limit. It stops when the
/// match is over.
/// </summary>
/// <param name="ElapsedTicks">Ticks played.</param>
/// <param name="LimitTicks">The length of the match.</param>
/// <param name="IsSuddenDeath">The clock has run out with the top score tied: the match goes on until one tank leads alone.</param>
public readonly record struct MatchClock(bool Active, int ElapsedTicks, int LimitTicks, bool IsSuddenDeath)
{
    /// <summary>Ticks left on the clock; 0 from the limit on.</summary>
    public int RemainingTicks => Math.Max(0, LimitTicks - ElapsedTicks);

    /// <summary>Seconds left on the clock, rounded up, so it reads 0 only once the clock has run out.</summary>
    public int RemainingSeconds => (RemainingTicks + Tweaks.Perf.TargetFps - 1) / Tweaks.Perf.TargetFps;

    /// <summary>The length of the match in seconds (<see cref="MatchRules.TimeLimitSeconds"/>).</summary>
    public int LimitSeconds => LimitTicks / Tweaks.Perf.TargetFps;

    /// <summary>Ticks played past the limit, in sudden death.</summary>
    public int OvertimeTicks => Math.Max(0, ElapsedTicks - LimitTicks);
}
