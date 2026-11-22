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
    [Fact]
    public void ANestWhoseCentreIsBuried_IsFound_WhenItsGroundComesIntoSight()
    {
        var (world, tank) = Arena();
        // The centre lies 12 cells into the dirt beyond the pocket's edge, deeper than sight reaches into solid
        // ground; the nest's ground reaches the pocket.
        var nest = world.Emberites!.AddNest(Centre + new Offset(24, 0), radius: 14, creatures: 6);

        Tick(world, tank, 2);

        var vision = world.TrackVision(tank);
        Assert.False(vision.IsVisible(nest.Centre));
        Assert.True(vision.IsVisible(Centre + new Offset(11, 0)));
        var danger = Assert.Single(Dangers(world, tank));
        Assert.Equal(nest.Centre, danger.Centre);
        Assert.Equal(6, Emitted(world).Rings.Count);
    }

    /// <summary>
    /// The report this guards: a tank in a tunnel beside a generated lump sees the lump's face (and the radar's square
    /// echo of the nest) but not the deposit's first cell, which is the nest's centre, and so never got the ring. The
    /// tunnel runs just outside the nest's circle (which reaches past the lump's ore), so the ring comes from seeing the
    /// nest's own solid ground from beside it, not open ground dug into it.
    /// </summary>
    [Fact]
    public void ALumpSeenFromATunnelBesideIt_ShowsItsNestRing()
    {
        var world = Emberward.Core.Match.MatchSetup.Build(new Size(320, 200), TestHelpers.DefaultSeed,
            Emberward.Core.Match.MatchRules.Standard with { Emberites = true, TerrainRegrowth = false });
        var lump = world.OreDeposits.First(d => d.Kind == Emberward.Core.LevelGen.OreDepositKind.Lump);
        int nestIndex = Enumerable.Range(0, world.Emberites!.Nests.Count).Single(i => world.Emberites.Nests[i].Centre == lump.Center);
        var nest = world.Emberites.Nests[nestIndex];
        // A tunnel seven cells wide running past the lump, its near edge one cell outside the nest's circle.
        int tunnelY = lump.Center.Y + nest.Radius + 4;
        for (int x = lump.Center.X - 30; x <= lump.Center.X + 30; x++)
            for (int y = tunnelY - 3; y <= tunnelY + 3; y++)
                if (Pixel.IsSoil(world.Terrain.GetPixelRaw(new Position(x, y))))
                    world.Terrain.SetPixel(new Position(x, y), TerrainPixel.Blank);
        var tank = world.TankList.Tanks[0];
        tank.Position = new Position(lump.Center.X, tunnelY);
        var vision = world.TrackVision(tank);

        Tick(world, tank, 3);

        Assert.False(vision.IsVisible(lump.Center));
        Assert.True(SolidGroundInSight(world, vision, nest.Centre, nest.Radius), "the tank should see some of the nest's solid ground");
        Assert.Contains(Dangers(world, tank), d => d.Centre == nest.Centre);
    }

    /// <summary>Whether a cell within <paramref name="radius"/> of <paramref name="around"/> that is not open ground is in sight.</summary>
    private static bool SolidGroundInSight(World world, Emberward.Core.Sensing.VisionMap vision, Position around, int radius)
    {
        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                var cell = around + new Offset(x, y);
                if (x * x + y * y <= radius * radius && vision.IsVisible(cell) && !Pixel.IsOpenGround(world.Terrain.GetPixelRaw(cell)))
                    return true;
            }
        return false;
    }

    /// <summary>
    /// What finding nests costs follows what changes in sight: with a still tank and still ground, a nest out of sight
    /// costs no cell reads tick after tick, and nor does one in sight.
    /// </summary>
    [Fact]
    public void NestsInAndOutOfSight_CostNoCellReads_WhileVisionStaysAsItIs()
    {
        var (world, tank) = Arena();
        world.Emberites!.AddNest(Centre + new Offset(45, 0), radius: 12, creatures: 6);
        world.Emberites.AddNest(Centre + new Offset(8, 0), radius: 4, creatures: 6);
        var vision = world.TrackVision(tank);
        Tick(world, tank, 3);
        Assert.Single(Dangers(world, tank));

        long before = vision.AreaCellReads;
        Tick(world, tank, 60);

        Assert.Equal(before, vision.AreaCellReads);
        Assert.Single(Dangers(world, tank));
    }

    /// <summary>The remembered answers are the answers a full look gives, as the tank moves and the ground is dug.</summary>
    [Fact]
    public void TheRememberedAreaAnswers_MatchAFullLook()
    {
        var (world, tank) = Arena();
        var vision = world.TrackVision(tank);
        (Position Centre, int Radius)[] areas = [(Centre + new Offset(20, 0), 4), (Centre + new Offset(30, 5), 9), (Centre + new Offset(-25, -8), 6)];
        for (int tick = 0; tick < 80; tick++)
        {
            tank.Position = Centre + new Offset(tick % 40 - 20, (tick / 40) * 6 - 3);
            if (tick == 50)
                for (int x = 12; x <= 40; x++)
                    for (int y = -2; y <= 2; y++)
                        world.Terrain.SetPixel(Centre + new Offset(x, y), TerrainPixel.Blank);
            Tick(world, tank, 1);
            foreach (var (centre, radius) in areas)
            {
                Assert.Equal(FullLook(vision, centre, radius, explored: false), vision.IsAnyVisibleWithin(centre, radius));
                Assert.Equal(FullLook(vision, centre, radius, explored: true), vision.IsAnyExploredWithin(centre, radius));
            }
        }
    }

    private static bool FullLook(Emberward.Core.Sensing.VisionMap vision, Position centre, int radius, bool explored)
    {
        const int stride = Emberward.Core.Sensing.VisionMap.AreaStride;
        for (int y = centre.Y - radius; y <= centre.Y + radius; y++)
            for (int x = centre.X - radius; x <= centre.X + radius; x++)
            {
                int dx = x - centre.X, dy = y - centre.Y;
                if (dx % stride != 0 || dy % stride != 0 || dx * dx + dy * dy > radius * radius)
                    continue;
                var cell = new Position(x, y);
                if (explored ? vision.IsExplored(cell) : vision.IsVisible(cell))
                    return true;
            }
        return false;
    }
}
