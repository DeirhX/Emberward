namespace Emberward.Tests;

using Emberward.Browser.Client;
using Emberward.Core.LevelGen;
using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Desktop;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Net;
using Emberward.Protocol;

/// <summary>
/// The match mode on its way from a command line, an address or a room to the rules a world is built with. What a
/// mode's rules are is Core's (<see cref="MatchRules.ForMode"/>); these tests are about every host asking it, and
/// about a standard match staying the match each host had before there were modes.
/// </summary>
public class MatchModeWiringTests
{
    private static readonly Size Small = new(320, 200);
    private static readonly Size Medium = new(480, 300);

    /// <summary>A local match on the desktop before modes: the standard rules with the swarm, won by the last base.</summary>
    private static readonly MatchRules DesktopLocal = MatchRules.Standard with { CoreTapWins = false, Emberites = true };

    /// <summary>A network match, and the browser's local one, before modes: the same, and a race to the core.</summary>
    private static readonly MatchRules Race = MatchRules.Standard with { CoreTapWins = true, Emberites = true };

    [Fact]
    public void AStandardMatch_HasTheRulesEachHostHadBeforeModes()
    {
        var config = NetMatch.ProposeConfig(5, Small, LevelGenMode.Deterministic, players: 2);

        Assert.Equal(DesktopLocal, new WorldLaunchOptions(false, Small, LevelGenMode.Deterministic, Seed: 5).Rules);
        Assert.Equal(Race, new WorldLaunchOptions(false, Small, LevelGenMode.Deterministic, Seed: 5, CoreTapWins: true).Rules);
        Assert.Equal(Race, NetMatch.RulesOf(config));
        Assert.Equal(Race, MatchRecording.BuildWorld(config).Rules);
        Assert.Equal(Race, BrowserSession.ForNetwork(config, localSlot: 1).World.Rules);
        Assert.Equal(Race, new BrowserSession(5, mapSize: Small).World.Rules);
    }

    [Fact]
    public void AnArcadeMatch_HasTheRulesCoreGivesTheMode_OnEveryHost()
    {
        var config = NetMatch.ProposeConfig(5, Small, LevelGenMode.Deterministic, players: 2, MatchMode.Arcade);
        var local = MatchRules.ForMode(MatchMode.Arcade, coreTapWins: false);
        var net = MatchRules.ForMode(MatchMode.Arcade, coreTapWins: true);

        Assert.Equal(1, config.Mode);
        Assert.Equal(MatchMode.Arcade, NetMatch.ModeOf(config));
        Assert.Null(NetMatch.CannotBuild(config));
        Assert.Equal(local, new WorldLaunchOptions(false, Small, LevelGenMode.Deterministic, Seed: 5, Mode: MatchMode.Arcade).Rules);
        Assert.Equal(net, NetMatch.RulesOf(config));
        Assert.Equal(net, MatchRecording.BuildWorld(config).Rules);
        var browser = BrowserSession.ForNetwork(config, localSlot: 0);
        Assert.Equal((net, MatchMode.Arcade), (browser.World.Rules, browser.Mode));
        var solo = new BrowserSession(5, mapSize: Small, mode: MatchMode.Arcade);
        Assert.Equal((net, MatchMode.Arcade), (solo.World.Rules, solo.Mode));
    }

    [Fact]
    public void ARoomsSettings_NameTheirModeOnlyToAClientThatKnowsIt()
    {
        var config = NetMatch.ProposeConfig(5, Medium, LevelGenMode.Deterministic, players: 4);

        Assert.Equal(0, config.Mode);
        Assert.Equal(MatchMode.Standard, NetMatch.ModeOf(config));
        Assert.Null(NetMatch.CannotBuild(config));
        Assert.Equal("Standard match   4 tanks   map 480x300", LobbyText.SettingsLine(config));
        Assert.Equal("Arcade match   4 tanks   map 480x300", LobbyText.SettingsLine(config with { Mode = 1 }));
        Assert.Contains("match mode (7)", NetMatch.CannotBuild(config with { Mode = 7 }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MatchMode.Standard, 2, MapPreset.Small)]
    [InlineData(MatchMode.Standard, 3, MapPreset.Medium)]
    [InlineData(MatchMode.Standard, 4, MapPreset.Medium)]
    [InlineData(MatchMode.Standard, 8, MapPreset.Medium)]
    [InlineData(MatchMode.Arcade, 2, MapPreset.Small)]
    [InlineData(MatchMode.Arcade, 3, MapPreset.Small)]
    [InlineData(MatchMode.Arcade, 4, MapPreset.Small)]
    [InlineData(MatchMode.Arcade, 5, MapPreset.Medium)]
    [InlineData(MatchMode.Arcade, 8, MapPreset.Medium)]
    public void TheMapNobodyChose_FollowsTheModeAndTheTanks(MatchMode mode, int players, MapPreset expected)
    {
        Assert.Equal(expected, MapPresets.DefaultFor(mode, players));
        Assert.Equal(MapPresets.SizeOf(expected), MapPresets.DefaultSizeFor(mode, players));
        // A room opened from the command line, from the menu or from the browser asks the same rule.
        var proposed = BrowserNetMatch.Propose(7, players, mode: mode);
        Assert.Equal(MapPresets.SizeOf(expected), new Size(proposed.MapWidth, proposed.MapHeight));
    }

    [Fact]
    public void ModeNames_ReadBackAsTheyAreWritten()
    {
        Assert.Equal([MatchMode.Standard, MatchMode.Arcade], MatchModes.All);
        foreach (var mode in MatchModes.All)
        {
            Assert.Equal(mode, MatchModes.Parse(MatchModes.Name(mode)));
            Assert.Equal(mode, MatchModes.Parse(" " + MatchModes.Label(mode).ToUpperInvariant() + " "));
            Assert.NotEmpty(MatchModes.RoomHint(mode));
        }
        Assert.Null(MatchModes.Parse(null));
        Assert.Null(MatchModes.Parse(""));
        Assert.Null(MatchModes.Parse("1"));
        Assert.Equal(MatchMode.Arcade, MatchModes.Step(MatchMode.Standard, +1));
        Assert.Equal(MatchMode.Arcade, MatchModes.Step(MatchMode.Standard, -1));
        Assert.Equal(MatchMode.Standard, MatchModes.Step(MatchMode.Arcade, +1));
    }

    // ---- The browser's address ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("arcade", MatchMode.Arcade)]
    [InlineData("ARCADE", MatchMode.Arcade)]
    [InlineData("standard", MatchMode.Standard)]
    [InlineData("", MatchMode.Standard)]
    [InlineData(null, MatchMode.Standard)]
    [InlineData("deathmatch", MatchMode.Standard)]
    public void TheAddressesMode_IsReadAsTheCommandLineReadsIt(string? text, MatchMode expected) =>
        Assert.Equal(expected, BrowserLaunch.Mode(text));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(9, 2)]
    [InlineData(-3, 2)]
    public void TheAddressesPlayers_AreTwoToEight_AndTwoOtherwise(int asked, int expected) =>
        Assert.Equal(expected, BrowserLaunch.Players(asked));

    [Fact]
    public void ALocalBrowserMatchOfFour_HasABotInEveryTankButThePlayers()
    {
        var session = new BrowserSession(5, mapSize: Small, players: 4);

        Assert.Equal(4, session.World.TankList.Count);
        Assert.Equal(0, session.LocalSlot);
        Assert.Equal([false, true, true, true], session.World.TankBases.Bases.Select(b => b.IsBotControlled));
        // The bots play: a few steps in, the match runs with the player standing still.
        for (int i = 0; i < 5; i++)
            session.Advance(default);
        Assert.Equal(5, session.World.AdvanceCount);
    }

    [Fact]
    public void ALocalBrowserMatch_KeepsItsLargeMap_UnlessItIsArcade()
    {
        Assert.Equal(BrowserSession.DefaultMapSize, BrowserSession.LocalMapSize(MatchMode.Standard, 2));
        Assert.Equal(BrowserSession.DefaultMapSize, BrowserSession.LocalMapSize(MatchMode.Standard, 4));
        Assert.Equal(Small, BrowserSession.LocalMapSize(MatchMode.Arcade, 4));
        Assert.Equal(Medium, BrowserSession.LocalMapSize(MatchMode.Arcade, 5));

        var arcade = new BrowserSession(5, players: 4, mode: MatchMode.Arcade);
        Assert.Equal((Small, 4), (arcade.MapSize, arcade.World.TankList.Count));
        // A count the address cannot mean is the normal two.
        Assert.Equal(2, new BrowserSession(5, mapSize: Small, players: 99).World.TankList.Count);
    }

    // ---- What the end of a match says ----------------------------------------------------------------------------------

    [Fact]
    public void TheDefeatLine_SpeaksOfTheEnemyOnlyWhenThereIsOne()
    {
        var duel = MatchSetup.Build(Small, 5, DesktopLocal);
        var four = MatchSetup.Build(Medium, 5, DesktopLocal, players: 4);

        Assert.Equal("The enemy base is the last standing", MatchOverlay.DefeatLine(duel, duel.TankList.Tanks[0]));
        Assert.Equal("Another base is the last standing", MatchOverlay.DefeatLine(four, four.TankList.Tanks[0]));
        Assert.Equal("BOT 3's base is the last standing", MatchOverlay.DefeatLine(four, four.TankList.Tanks[0], "BOT 3"));
    }
}

/// <summary>The desktop's world for a mode and a number of tanks (<see cref="WorldFactory"/> writes to the console).</summary>
[Collection(ConsoleTestGroup.Name)]
public class MatchModeWorldFactoryTests
{
    [Theory]
    [InlineData(MatchMode.Standard, 2, 320, 200)]
    [InlineData(MatchMode.Standard, 4, 480, 300)]
    [InlineData(MatchMode.Arcade, 4, 320, 200)]
    [InlineData(MatchMode.Arcade, 5, 480, 300)]
    public void ALocalMatchWithoutASize_GetsTheMapOfItsModeAndTanks(MatchMode mode, int players, int width, int height)
    {
        var built = WorldFactory.Build(new WorldLaunchOptions(
            TerrainTest: false, TerrainSizeOverride: null, LevelGenMode.Optimized, Seed: 5, Players: players, Mode: mode));

        Assert.Equal(new Size(width, height), built.TerrainSize);
        Assert.Equal(players, built.World.TankList.Count);
        Assert.Equal(MatchRules.ForMode(mode, coreTapWins: false), built.World.Rules);
    }

    [Fact]
    public void AChosenSize_IsKeptWhateverTheMode()
    {
        var built = WorldFactory.Build(new WorldLaunchOptions(
            TerrainTest: false, new Size(480, 300), LevelGenMode.Optimized, Seed: 5, Players: 4, Mode: MatchMode.Arcade));

        Assert.Equal(new Size(480, 300), built.TerrainSize);
    }
}
