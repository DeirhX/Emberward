using Emberward.Core;
using Emberward.Core.Collision;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// The emberites hunt every tank by the same rules, whoever drives it (DESIGN.md §6.2): a bot's tank is sensed, chased
/// and bitten by its heat as a player's is, and a tank docked in its own base is left alone whoever drives it.
/// </summary>
public class EmberiteBotTargetTests
{
    // Clear of both bases of the 320 x 200 test map, which stand at (130, 21) and (185, 178).
    private static readonly Position Centre = new(60, 100);

    private static World Arena()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        for (int y = -30; y <= 30; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(Centre + new Offset(x, y), TerrainPixel.Blank);
        return world;
    }

    /// <summary>Steps with each tank held at its heat; returns the colours bitten.</summary>
    private static List<int> Tick(World world, int ticks, float heat0, float heat1)
    {
        var bitten = new List<int>();
        var tanks = world.TankList.Tanks;
        for (int i = 0; i < ticks; i++)
        {
            tanks[0].Heat = heat0;
            tanks[1].Heat = heat1;
            world.Advance(_ => default);
            foreach (var e in world.StepEvents.ToArray())
                if (e.Kind == StepEventKind.EmberiteBit)
                    bitten.Add(e.Color);
        }
        return bitten;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheHotTank_IsHunted_WhetherAPlayerOrABotDrivesIt(int hotSlot)
    {
        var world = Arena();
        // Tank 1 is driven by a bot, tank 0 by a player.
        world.SetBotControlled(1, true);
        Assert.True(world.TankList.Tanks[1].Base!.IsBotControlled);
        Assert.False(world.TankList.Tanks[0].Base!.IsBotControlled);

        var tanks = world.TankList.Tanks;
        var hot = tanks[hotSlot];
        var cool = tanks[1 - hotSlot];
        world.Emberites!.AddNest(Centre + new Offset(40, 0), radius: 4, creatures: 6);
        hot.Position = Centre;
        cool.Position = Centre + new Offset(-45, 20);

        var bitten = hotSlot == 0 ? Tick(world, 240, heat0: 80f, heat1: 10f) : Tick(world, 240, heat0: 10f, heat1: 80f);

        Assert.Contains(hot.Color, bitten);
        Assert.DoesNotContain(cool.Color, bitten);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ATankDockedInItsOwnBase_IsLeftAlone_WhetherAPlayerOrABotDrivesIt(int slot)
    {
        var world = Arena();
        world.SetBotControlled(1, true);
        var tanks = world.TankList.Tanks;
        var tank = tanks[slot];
        var other = tanks[1 - slot];
        var home = tank.Base!;

        // A nest straight out from the gate facing the map's middle, with open ground between.
        int toward = home.Position.Y < world.Terrain.Height / 2 ? 1 : -1;
        var inside = home.Position + new Offset(0, toward * BaseLayout.Lane);
        var nestAt = home.Position + new Offset(0, toward * (BaseLayout.Half + 18));
        for (int d = BaseLayout.Half + 1; d <= BaseLayout.Half + 30; d++)
            for (int x = -12; x <= 12; x++)
                world.Terrain.SetPixel(home.Position + new Offset(x, toward * d), TerrainPixel.Blank);
        var nest = world.Emberites!.AddNest(nestAt, radius: 4, creatures: 6);
        other.Position = Centre;

        tank.Position = inside;
        Assert.True(tank.IsDockedAtOwnBase);
        float hotHeat = 80f;
        Assert.True(Position.DistanceSquared(inside, nestAt) <= Math.Pow(Emberward.Core.Entities.Emberites.EmberiteSwarm.SenseRange(hotHeat), 2),
            "the docked tank must be within the nest's sense range, so only being docked keeps it asleep");
        var bitten = slot == 0 ? Tick(world, 120, heat0: hotHeat, heat1: 10f) : Tick(world, 120, heat0: 10f, heat1: hotHeat);
        Assert.False(nest.Awake, "a tank docked in its own base should not wake a nest");
        Assert.Empty(bitten);

        // The same tank just outside its gate: hunted.
        tank.Position = home.Position + new Offset(0, toward * (BaseLayout.Half + 5));
        Assert.False(tank.IsDockedAtOwnBase);
        bitten = slot == 0 ? Tick(world, 240, heat0: hotHeat, heat1: 10f) : Tick(world, 240, heat0: 10f, heat1: hotHeat);
        Assert.True(nest.Awake);
        Assert.Contains(tank.Color, bitten);
    }
}
