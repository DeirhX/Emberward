namespace Emberward.Desktop.Net;

using System.Collections.Immutable;
using System.Globalization;
using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.LevelGen;
using Emberward.Core.Snapshots;
using Emberward.Core.Types;
using Emberward.Protocol;

/// <summary>
/// A network match as data: its settings, which slots players drove, and every tick's inputs. That is all a match
/// is, so replaying it rebuilds the exact world, which is how a desync is examined afterwards. A player who joined
/// the match in progress records from the snapshot it loaded (docs/late-join-design.md §6.2).
/// </summary>
/// <remarks>
/// As text: <c>key=value</c> header lines, then one line per tick with each slot's input in hexadecimal. The header's
/// <c>mode</c> is the room's match mode, which decides the rules the replay builds its world with; a recording without
/// the line is of a standard match. A line
/// <c>mask=</c> among the ticks gives the human mask (in hexadecimal) from the next tick on; the header's
/// <c>humanMask</c> is the mask before the first. A recording from a snapshot has it in the header as
/// <c>snapshot=</c>, compressed and in base 64, with <c>firstTick</c>, the tick it was saved at. Lines starting with
/// <c>#</c> are notes and are skipped on loading.
/// <para>
/// A recording holds the players' inputs and none of the bots': a replay has the bots decide again. The header's
/// <c>bots</c> is the <see cref="CurrentBots"/> of the build that recorded it (1 without the line). A recording a bot
/// played in, from a build whose bots decided otherwise, cannot be replayed, and <see cref="Replay"/> says so. A
/// recording of players only is not checked by that number, but it is not proof against other changes either: it
/// carries no version of the simulation, so it replays the same match only on a build whose simulation gives the same
/// result for the same inputs (one with the same <c>SimulationFingerprintTests</c> constants). On a build that changed
/// the simulation it replays without complaint into another match.
/// </para>
/// </remarks>
/// <param name="HumanMask">Which slots players drove before the first recorded tick.</param>
/// <param name="Bundles">Every tick from the first recorded one, each with its human mask.</param>
/// <param name="Snapshot">The compressed snapshot the recording starts from, or null for one from tick 0.</param>
public sealed record MatchRecording(
    MatchConfig Config, int HumanMask, IReadOnlyList<TickBundle> Bundles, byte[]? Snapshot = null)
{
    /// <summary>
    /// Which bots a recording made by this build was played with. Raise it whenever a bot would decide otherwise in
    /// the same match, so an older recording with bots fails to replay instead of replaying another match. 1: a bot
    /// was asked during the step, after the tanks in the slots before it had moved. 2: every bot decides before the
    /// step (<see cref="SlotStepper"/>).
    /// </summary>
    public const int CurrentBots = 2;

    /// <summary>The <see cref="CurrentBots"/> of the build that recorded this.</summary>
    public int Bots { get; init; } = CurrentBots;

    /// <summary>Whether a bot drove a tank at any recorded tick.</summary>
    public bool HasBots
    {
        get
        {
            int everySlot = (1 << Config.Slots) - 1;
            if ((HumanMask & everySlot) != everySlot)
                return true;
            foreach (TickBundle bundle in Bundles)
                if ((bundle.HumanMask & everySlot) != everySlot)
                    return true;
            return false;
        }
    }

    public void Save(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var inv = CultureInfo.InvariantCulture;
        writer.WriteLine(string.Create(inv, $"seed={Config.Seed}"));
        writer.WriteLine(string.Create(inv, $"map={Config.MapWidth}x{Config.MapHeight}"));
        writer.WriteLine(string.Create(inv, $"genMode={Config.GenMode}"));
        writer.WriteLine(string.Create(inv, $"slots={Config.Slots}"));
        writer.WriteLine(string.Create(inv, $"ticksPerSecond={Config.TicksPerSecond}"));
        writer.WriteLine(string.Create(inv, $"inputKeyMask={Config.InputKeyMask:X}"));
        writer.WriteLine(string.Create(inv, $"mode={Config.Mode}"));
        writer.WriteLine(string.Create(inv, $"bots={Bots}"));
        writer.WriteLine(string.Create(inv, $"humanMask={HumanMask}"));
        if (Snapshot != null)
        {
            writer.WriteLine(string.Create(inv, $"firstTick={(Bundles.Count > 0 ? Bundles[0].Tick : 0)}"));
            writer.WriteLine("snapshot=" + Convert.ToBase64String(Snapshot));
        }
        writer.WriteLine("ticks:");
        int mask = HumanMask;
        foreach (TickBundle bundle in Bundles)
        {
            if (bundle.HumanMask != mask)
            {
                mask = bundle.HumanMask;
                writer.WriteLine(string.Create(inv, $"mask={mask:X}"));
            }
            writer.WriteLine(string.Join(' ', bundle.Inputs.Select(input => input.ToString("X", inv))));
        }
    }

    /// <exception cref="InvalidDataException">The text is not a recording, or it is of a match mode this build does not know.</exception>
    public static MatchRecording Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        var bundles = new List<TickBundle>();
        bool inTicks = false;
        const string MaskLine = "mask=";
        try
        {
            int Number(string key) => int.Parse(header[key], CultureInfo.InvariantCulture);
            int firstTick = 0, mask = 0;
            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                if (inTicks && line.StartsWith(MaskLine, StringComparison.Ordinal))
                    mask = int.Parse(line.AsSpan(MaskLine.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                else if (inTicks)
                {
                    var inputs = line.Split(' ').Select(x => uint.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    bundles.Add(new TickBundle(firstTick + bundles.Count, [.. inputs], mask));
                }
                else if (line == "ticks:")
                {
                    inTicks = true;
                    mask = Number("humanMask");
                    firstTick = header.ContainsKey("firstTick") ? Number("firstTick") : 0;
                }
                else if (line.Split('=', 2) is [var key, var value])
                    header[key] = value;
            }

            string[] map = header["map"].Split('x');
            var config = new MatchConfig(
                Number("seed"), int.Parse(map[0], CultureInfo.InvariantCulture), int.Parse(map[1], CultureInfo.InvariantCulture),
                Number("genMode"), Number("slots"), Number("ticksPerSecond"),
                uint.Parse(header["inputKeyMask"], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                // A recording from before matches had modes has no such line: it is of a standard match.
                header.TryGetValue("mode", out string? mode) ? byte.Parse(mode, CultureInfo.InvariantCulture) : (byte)0);
            if (!Enum.IsDefined(NetMatch.ModeOf(config)))
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"This recording is of match mode {config.Mode}, which this build does not know."));
            byte[]? snapshot = header.TryGetValue("snapshot", out string? base64) ? Convert.FromBase64String(base64) : null;
            // A recording from before bots had a number is of the first.
            return new MatchRecording(config, Number("humanMask"), bundles, snapshot) { Bots = header.ContainsKey("bots") ? Number("bots") : 1 };
        }
        catch (Exception e) when (e is KeyNotFoundException or FormatException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("This is not a match recording: " + e.Message, e);
        }
    }

    /// <summary>The world a client builds for these settings, before the first tick.</summary>
    public static World BuildWorld(MatchConfig config) => WorldFactory.Build(new WorldLaunchOptions(
        TerrainTest: false, new Size(config.MapWidth, config.MapHeight), (LevelGenMode)config.GenMode, config.Seed,
        Players: config.Slots, CoreTapWins: true, Mode: NetMatch.ModeOf(config))).World;

    /// <summary>
    /// Plays the match again from the start (or from the snapshot) and returns the world after the first
    /// <paramref name="ticks"/> recorded ticks (all of them by default), with the bots playing in it then.
    /// <paramref name="afterTick"/> is called after each one.
    /// </summary>
    /// <exception cref="InvalidDataException">A bot played in it, and this build's bots are not the ones it was recorded with.</exception>
    public (World World, IReadOnlyList<BotTankAI?> Bots) Replay(int? ticks = null, Action<World>? afterTick = null)
    {
        if (Bots != CurrentBots && HasBots)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"This recording was played with bots of version {Bots} and this build has version {CurrentBots}: its bots would not decide as they did, so the replay would be another match. Replay it with the build that recorded it."));
        World world = BuildWorld(Config);
        Seats seats = Snapshot is { } snapshot
            ? new Seats(Config.Seed, WorldSnapshot.Load(world, WorldSnapshot.Decompress(snapshot)))
            : Seats.ForStart(world, Config.Seed, HumanMask);
        int count = Math.Min(ticks ?? Bundles.Count, Bundles.Count);
        for (int i = 0; i < count; i++)
        {
            seats.Step(world, Bundles[i]);
            afterTick?.Invoke(world);
        }
        return (world, seats.Bots);
    }

    /// <summary>The same recording cut to its first <paramref name="ticks"/> ticks.</summary>
    public MatchRecording Truncated(int ticks) => this with { Bundles = Bundles.Take(ticks).ToImmutableArray() };
}
