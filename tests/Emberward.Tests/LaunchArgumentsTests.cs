namespace Emberward.Tests;

using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Desktop.Config;
using Emberward.Desktop.Net;

public class LaunchArgumentsTests
{
    private static LaunchArguments Parse(string[] args, out List<string> warnings, Dictionary<string, string>? env = null)
    {
        var warned = new List<string>();
        var parsed = LaunchArguments.Parse(args, name => env?.GetValueOrDefault(name), warned);
        warnings = warned;
        return parsed;
    }

    private static LaunchArguments Parse(params string[] args) => Parse(args, out _);

    [Fact]
    public void NoArguments_IsALocalMatchWithTheDefaults()
    {
        var launch = Parse(args: [], out var warnings);

        Assert.Empty(warnings);
        Assert.Null(launch.Net);
        Assert.Null(launch.TerrainSize);
        Assert.Null(launch.Seed);
        Assert.True(launch.EnableAiTanks);
        Assert.True(launch.FogOfWar);
        Assert.Equal("lobby", launch.Room);
        Assert.Equal(NetworkTweaks.DefaultPlayers, launch.Players);
        Assert.Equal(2, launch.LocalPlayers);
        Assert.Equal(MatchMode.Standard, launch.Mode);
        Assert.Equal(Environment.UserName, launch.Name);
        Assert.Equal(string.Empty, launch.Secret);
    }

    [Fact]
    public void MatchOptions_ParseAsTheyAlwaysDid()
    {
        Assert.Equal(new Size(1500, 750), Parse("--large").TerrainSize);
        Assert.Equal(new Size(640, 480), Parse("--size=640x480").TerrainSize);
        Assert.Null(Parse("--size=640").TerrainSize);
        Assert.Equal(new Size(320, 200), Parse("--size=320x200").TerrainSize);
        Assert.Null(Parse("--size=100x100").TerrainSize);
        Assert.Null(Parse("--size=320x199").TerrainSize);
        Assert.True(Parse("--fast").FastGen);
        Assert.False(Parse("--no-ai").EnableAiTanks);
        Assert.False(Parse("--disable-ai").EnableAiTanks);
        Assert.False(Parse("--no-fog").FogOfWar);
        Assert.Equal(7, Parse("--seed=7").Seed);
        Assert.True(Parse("--energy-audit").EnergyAudit);
        Assert.True(Parse("--terrain-test").TerrainTest);
    }

    [Fact]
    public void Arcade_IsAskedForByItsFlagOrByName()
    {
        Assert.Equal(MatchMode.Arcade, Parse("--arcade").Mode);
        Assert.Equal(MatchMode.Arcade, Parse("--mode=arcade").Mode);
        Assert.Equal(MatchMode.Arcade, Parse("--mode=Arcade").Mode);
        Assert.Equal(MatchMode.Standard, Parse("--mode=standard").Mode);
        Assert.Equal(MatchMode.Standard, Parse("--arcade", "--mode=standard").Mode);

        // A mode nobody knows leaves the one already asked for.
        var unknown = Parse(["--arcade", "--mode=deathmatch"], out var warnings);
        Assert.Equal(MatchMode.Arcade, unknown.Mode);
        Assert.Contains("deathmatch", Assert.Single(warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Players_AreTheTanksOfALocalMatchToo_AndEachKindOfMatchKeepsItsDefault()
    {
        // Without the option a local match has two tanks and a room four.
        Assert.Equal((2, 4), (Parse().LocalPlayers, Parse().Players));
        Assert.Equal((2, 4), (Parse("--net").LocalPlayers, Parse("--net").Players));

        var four = Parse("--players=4", "--arcade");
        Assert.Null(four.Net);
        Assert.Equal((4, 4), (four.LocalPlayers, four.Players));
        Assert.Equal(8, Parse("--players=8").LocalPlayers);
        Assert.Equal(2, Parse("--players=9").LocalPlayers);
    }

    [Fact]
    public void ANetworkLaunch_ProposesItsMode()
    {
        Assert.Equal(MatchMode.Standard, Parse("--net").Net!.Mode);
        var arcade = Parse("--net=friday", "--arcade", "--players=3").Net!;
        Assert.Equal((MatchMode.Arcade, 3), (arcade.Mode, arcade.Players));
        Assert.Contains("--mode=arcade", arcade.ToArguments());
        Assert.DoesNotContain(Parse("--net").Net!.ToArguments(), a => a.StartsWith("--mode", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplayArguments_NameWhatTheMapDependsOn_AndTheCommandLineReadsThemBack()
    {
        Assert.Equal("--seed=7", LaunchArguments.ReplayArguments(7, fastGen: false, players: 2, MatchMode.Standard));
        Assert.Equal("--fast --seed=7", LaunchArguments.ReplayArguments(7, fastGen: true, players: 2, MatchMode.Standard));
        Assert.Equal(
            "--fast --seed=-7 --players=4 --arcade",
            LaunchArguments.ReplayArguments(-7, fastGen: true, players: 4, MatchMode.Arcade));

        foreach (var mode in MatchModes.All)
        {
            var replay = Parse(LaunchArguments.ReplayArguments(-7, fastGen: true, players: 4, mode).Split(' '), out var warnings);

            Assert.Empty(warnings);
            Assert.Equal((-7, true, 4, mode), (replay.Seed, replay.FastGen, replay.LocalPlayers, replay.Mode));
        }
    }

    [Fact]
    public void DisplayAndSoundOptions_ParseAsTheyAlwaysDid()
    {
        Assert.Equal(DesktopDisplayOptions.DefaultWindowedSize, Parse("--windowed").WindowedSize);
        Assert.Equal(new Size(800, 600), Parse("--windowed=800x600").WindowedSize);
        Assert.Equal(2, Parse("--display=2").DisplayIndex);
        Assert.Equal(1, Parse("--display=secondary").DisplayIndex);
        Assert.True(Parse("--hidden").Hidden);
        Assert.Equal(0.4f, Parse("--volume=40").Volume);
        Assert.True(Parse("--mute").Mute);
        Assert.Equal("out", Parse("--dump-sounds=out").DumpSoundsDirectory);
    }

    [Fact]
    public void PerfOptions_ParseAsTheyAlwaysDid()
    {
        var launch = Parse("--perf", "--perf-warmup=10", "--perf-frames=20", "--perf-csv=a.csv");

        Assert.True(launch.Perf);
        Assert.Equal(10, launch.PerfWarmupFrames);
        Assert.Equal(20, launch.PerfMeasureFrames);
        Assert.Equal("a.csv", launch.PerfCsvPath);
    }

    [Fact]
    public void NetworkOptions_ParseAsTheyAlwaysDid()
    {
        var shared = Parse("--net");
        Assert.Equal(new Uri(NetworkTweaks.DefaultRelay), shared.Relay);
        Assert.Equal("lobby", shared.Room);

        var friday = Parse("--net=friday", "--name=Ann", "--players=3", "--secret=hush");
        Assert.Equal("friday", friday.Room);
        Assert.Equal("Ann", friday.Name);
        Assert.Equal(3, friday.Players);
        Assert.Equal("hush", friday.Secret);

        var own = Parse("--relay=ws://127.0.0.1:8130/play", "--room=lunch");
        Assert.Equal(new Uri("ws://127.0.0.1:8130/play"), own.Relay);
        Assert.Equal("lunch", own.Room);
        Assert.NotNull(own.Net);
    }

    [Fact]
    public void EnvironmentVariables_SetTheDefaultsAndArgumentsOverrideThem()
    {
        var env = new Dictionary<string, string>
        {
            ["EMBERWARD_RELAY_SECRET"] = "from-env",
            ["EMBERWARD_HIDDEN"] = "1",
            ["EMBERWARD_VOLUME"] = "20",
            ["EMBERWARD_DISPLAY"] = "3",
            ["EMBERWARD_MUTE"] = "yes",
        };

        var launch = Parse(["--volume=90"], out _, env);

        Assert.Equal("from-env", launch.Secret);
        Assert.True(launch.Hidden);
        Assert.Equal(0.9f, launch.Volume);
        Assert.Equal(3, launch.DisplayIndex);
        Assert.True(launch.Mute);
        Assert.Equal("from-arg", Parse(["--secret=from-arg"], out _, env).Secret);
    }

    [Theory]
    [InlineData("--players=9", "player count")]
    [InlineData("--players=1", "player count")]
    [InlineData("--seed=0", "seed")]
    [InlineData("--seed=abc", "seed")]
    [InlineData("--relay=http://example.com/play", "relay")]
    [InlineData("--volume=loud", "volume")]
    [InlineData("--display=left", "display")]
    [InlineData("--windowed=10x10", "window size")]
    [InlineData("--mode=", "mode")]
    [InlineData("--mode=deathmatch", "mode")]
    [InlineData("--frobnicate", "option")]
    public void AnOptionThatMakesNoSense_IsLeftOutWithAWarning(string arg, string mentions)
    {
        var launch = Parse([arg], out var warnings);

        Assert.Single(warnings);
        Assert.StartsWith("[Args] ", warnings[0], StringComparison.Ordinal);
        Assert.Contains(mentions, warnings[0], StringComparison.Ordinal);
        Assert.Equal(NetworkTweaks.DefaultPlayers, launch.Players);
        Assert.Equal(2, launch.LocalPlayers);
        Assert.Equal(MatchMode.Standard, launch.Mode);
        Assert.Null(launch.Relay);
        Assert.Null(launch.Seed);
    }

    public static TheoryData<NetLaunchOptions> NetworkMatches => new()
    {
        new NetLaunchOptions(new Uri("wss://crust.bokovka.cz/play"), "friday", "Ann", 4),
        new NetLaunchOptions(new Uri("ws://127.0.0.1:8130/play"), "lunch duel", "Ann B. \"The Digger\"", 2,
            MapSize: new Size(320, 200)),
        new NetLaunchOptions(new Uri("wss://relay.example/play"), "Ann's game", "Zoë", 8,
            MapSize: new Size(1500, 750), Seed: 123456),
        new NetLaunchOptions(new Uri("wss://relay.example/play"), "quick one", "Ann", 4,
            MapSize: new Size(320, 200), Mode: MatchMode.Arcade),
    };

    [Theory]
    [MemberData(nameof(NetworkMatches))]
    public void NetworkMatch_ToArgumentsThenParse_GivesTheSameMatch(NetLaunchOptions options)
    {
        var launch = Parse([.. options.ToArguments()], out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(options, launch.Net);
    }

    [Fact]
    public void Secret_IsNeverInTheArguments()
    {
        var options = new NetLaunchOptions(
            new Uri("wss://crust.bokovka.cz/play"), "friday", "Ann", 4, Secret: "hunter2-the-secret",
            MapSize: new Size(480, 300), Seed: 5);
        var launch = Parse("--volume=30", "--windowed");

        string[] direct = [.. options.ToArguments()];
        string[] relaunch = [.. launch.Relaunch(options)];

        foreach (string[] args in new[] { direct, relaunch })
        {
            Assert.DoesNotContain(args, a => a.Contains("hunter2", StringComparison.Ordinal));
            Assert.DoesNotContain(args, a => a.StartsWith("--secret", StringComparison.Ordinal));
        }
        Assert.Equal("EMBERWARD_RELAY_SECRET", NetLaunchOptions.SecretVariable);
    }

    [Fact]
    public void Secret_ComesBackFromTheEnvironmentOfTheNewProcess()
    {
        var options = new NetLaunchOptions(new Uri("wss://r.example/play"), "friday", "Ann", 4, Secret: "hush");
        var env = new Dictionary<string, string> { [NetLaunchOptions.SecretVariable] = options.Secret };

        var launch = Parse([.. options.ToArguments()], out _, env);

        Assert.Equal(options, launch.Net);
    }

    [Fact]
    public void Relaunch_KeepsDisplayAndSoundButNotTheMatch()
    {
        var launch = Parse(
            "--windowed=1024x600", "--display=1", "--volume=35", "--mute", "--no-ai", "--no-fog",
            "--seed=99", "--large", "--net=old-room", "--name=Old", "--fast", "--perf", "--arcade", "--players=6");

        var local = Parse([.. launch.Relaunch(null)], out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new Size(1024, 600), local.WindowedSize);
        Assert.Equal(1, local.DisplayIndex);
        Assert.Equal(0.35f, local.Volume);
        Assert.True(local.Mute);
        Assert.False(local.EnableAiTanks);
        Assert.False(local.FogOfWar);
        Assert.Null(local.Net);
        Assert.Null(local.Seed);
        Assert.Null(local.TerrainSize);
        Assert.Equal(MatchMode.Standard, local.Mode);
        Assert.Equal(2, local.LocalPlayers);
        Assert.False(local.FastGen);
        Assert.False(local.Perf);
        var chosen = new NetLaunchOptions(new Uri("wss://r.example/play"), "new-room", "Ann", 3, MapSize: new Size(480, 300));
        Assert.Equal(chosen, Parse([.. launch.Relaunch(chosen)]).Net);
    }
}
