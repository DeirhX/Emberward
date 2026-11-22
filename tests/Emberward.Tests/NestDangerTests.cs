using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Emberites;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>
/// The danger ring over a discovered nest (<see cref="World.CopyNestDangers"/>, <see cref="NestDangerQuads"/>): a nest
/// the side has seen, and that still has creatures, gets a small ring around its ground and a title; an undiscovered
/// nest gets nothing (a radar echo does not discover one), nor does one seen cleared; discovery outlasts the sight.
/// </summary>
public class NestDangerTests
{
    // Clear of both bases of the 320 x 200 test map, which stand at (130, 21) and (185, 178).
    private static readonly Position Centre = new(60, 100);

    /// <summary>A world with the swarm on and the player's vision tracked, its tank in an open pocket of dirt.</summary>
    private static (World World, Tank Tank) Arena(int halfWidth = 12)
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        for (int y = -30; y <= 30; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(Centre + new Offset(x, y), Math.Abs(x) <= halfWidth && Math.Abs(y) <= 10 ? TerrainPixel.Blank : TerrainPixel.Dirt);
        var tank = world.TankList.Tanks[0];
        tank.Position = Centre;
        world.TrackVision(tank);
        return (world, tank);
    }

    private static void Tick(World world, Tank tank, int ticks, float heat = 20f)
    {
        for (int i = 0; i < ticks; i++)
        {
            tank.Heat = heat;
            world.Advance(_ => default);
        }
    }

    private static NestDanger[] Dangers(World world, Tank tank)
    {
        var dangers = new NestDanger[world.Emberites!.Nests.Count];
        return dangers[..world.CopyNestDangers(tank, dangers)];
    }

    /// <summary>The ring and glyph quads the emitter adds for the world as it stands (one quad is six vertices).</summary>
    private static (List<EntityVertex> Rings, int Glyphs) Emitted(World world)
    {
        var emitter = new NestDangerQuads();
        emitter.CaptureStep(world, localSlot: 0);
        var quads = new EntityQuads();
        // A view over the whole test map, a cell a pixel, so nothing is culled.
        quads.Begin(new RenderView(new Size(320, 200), new Size(320, 200), default, 1), phase: 0f, alpha: 1f);
        emitter.EmitRings(quads);
        emitter.EmitLabels(quads);
        var vertices = quads.Vertices.AsSpan(0, quads.VertexCount).ToArray();
        return ([.. vertices.Where(v => v.Kind == EntityShape.KindDangerRing)],
            vertices.Count(v => v.Kind == EntityShape.KindPowerLabel) / 6);
    }

    [Fact]
    public void AnUndiscoveredNest_HasNoRing()
    {
        var (world, tank) = Arena();
        // Deep in the dirt, out of sight, and no radar.
        world.Emberites!.AddNest(Centre + new Offset(45, 0), radius: 4, creatures: 6);

        Tick(world, tank, 30);

        Assert.Empty(Dangers(world, tank));
        var (rings, glyphs) = Emitted(world);
        Assert.Empty(rings);
        Assert.Equal(0, glyphs);
    }

    [Fact]
    public void ANestInSight_GetsASmallRing_AndATitle()
    {
        var (world, tank) = Arena();
        world.Emberites!.AddNest(Centre + new Offset(8, 0), radius: 4, creatures: 6);

        Tick(world, tank, 2);

        var danger = Assert.Single(Dangers(world, tank));
        Assert.Equal(Centre + new Offset(8, 0), danger.Centre);
        // A small ring: the nest's ground plus a margin, not the distance it senses from.
        Assert.Equal(EmberiteSwarm.DangerRadius(4), danger.Radius);
        Assert.Equal(NestDangerLevel.Dormant, danger.Level);

        var (rings, glyphs) = Emitted(world);
        Assert.Equal(6, rings.Count);
        Assert.All(rings, v => Assert.Equal(danger.Radius, v.P0));
        Assert.All(rings, v => Assert.Equal((float)NestDangerLevel.Dormant, v.P1));
        int letters = (NestDangerQuads.Title + NestDangerQuads.Warning).Count(c => c != ' ');
        Assert.Equal(letters, glyphs);
    }

    [Fact]
    public void TheRing_PulsesWhileCreaturesAreOut()
    {
        var (world, tank) = Arena();
        world.Emberites!.AddNest(Centre + new Offset(8, 0), radius: 4, creatures: 6);

        Tick(world, tank, Tweaks.Emberites.StirTicks + 10, heat: 80f);

        var danger = Assert.Single(Dangers(world, tank));
        Assert.Equal(EmberiteSwarm.DangerRadius(4), danger.Radius);
        Assert.Equal(NestDangerLevel.Swarming, danger.Level);
    }

    [Fact]
    public void ANestOnlyTheRadarFound_HasNoRing()
    {
        var (world, tank) = Arena(halfWidth: 4);
        // Wide enough for the radar's rays to find it, out of the tank's sight through the dirt.
        var nest = world.Emberites!.AddNest(Centre + new Offset(40, 0), radius: 12, creatures: 6);
        world.TrackRadar(tank);

        Tick(world, tank, 8 + Tweaks.Radar.PulsePeriodTicks);
        Assert.False(world.TrackVision(tank).IsExplored(nest.Centre));
        var radar = world.RadarOf(tank)!;
        Assert.Contains(Enumerable.Range(0, radar.ContactCount),
            i => radar.ContactAt(i).Echo == Emberward.Core.Sensing.RadarEcho.Life && radar.ContactAt(i).Position == nest.Centre);
        Assert.Empty(Dangers(world, tank));
    }

    [Fact]
    public void ANestClearedOutOfSight_KeepsItsRing_UntilTheTankSeesItCleared()
    {
        var (world, tank) = Arena();
        var nest = world.Emberites!.AddNest(Centre + new Offset(8, 0), radius: 4, creatures: 6);
        Tick(world, tank, 2);
        Assert.Single(Dangers(world, tank));

        tank.Position = Centre + new Offset(-40, 0);
        Tick(world, tank, 2);
        Assert.False(world.TrackVision(tank).IsVisible(nest.Centre));
        nest.NeverReleased = 0;
        Tick(world, tank, 1);
        Assert.True(nest.IsCleared);
        Assert.Single(Dangers(world, tank));

        tank.Position = Centre;
        Tick(world, tank, 1);
        Assert.Empty(Dangers(world, tank));
    }

    [Fact]
    public void AClearedNest_HasNoRing()
    {
        var (world, tank) = Arena();
        var nest = world.Emberites!.AddNest(Centre + new Offset(8, 0), radius: 4, creatures: 6);
        Tick(world, tank, 2);
        Assert.Single(Dangers(world, tank));

        nest.NeverReleased = 0;
        Tick(world, tank, 1);

        Assert.True(nest.IsCleared);
        Assert.Empty(Dangers(world, tank));
        Assert.Empty(Emitted(world).Rings);
    }

    [Fact]
    public void ANestOutOfSight_ShowsWhatTheSideLastLearned_NotWhatItDoesNow()
    {
        var (world, tank) = Arena();
        var nest = world.Emberites!.AddNest(Centre + new Offset(8, 0), radius: 4, creatures: 6);
        Tick(world, tank, 2);
        Assert.Equal(NestDangerLevel.Dormant, Assert.Single(Dangers(world, tank)).Level);

        // The tank goes off into the dirt, out of sight of the nest, and the nest wakes.
        tank.Position = Centre + new Offset(-40, 0);
        Tick(world, tank, 2);
        Assert.False(world.TrackVision(tank).IsVisible(nest.Centre));
        nest.Awake = true;
        Tick(world, tank, 1);
        Assert.True(world.Emberites.TryGetDangerLevel(0, out var now) && now == NestDangerLevel.Awake);
        Assert.Equal(NestDangerLevel.Dormant, Assert.Single(Dangers(world, tank)).Level);

        // Back in sight: the ring shows it awake.
        tank.Position = Centre;
        Tick(world, tank, 1);
        Assert.Equal(NestDangerLevel.Awake, Assert.Single(Dangers(world, tank)).Level);
    }

    [Fact]
    public void WithoutFog_EveryNestIsKnown()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        var tank = world.TankList.Tanks[0];
        world.Emberites!.AddNest(Centre, radius: 4, creatures: 6);
        world.Emberites.AddNest(Centre + new Offset(60, 40), radius: 4, creatures: 6);

        Tick(world, tank, 1);

        Assert.Equal(2, Dangers(world, tank).Length);
    }
}
