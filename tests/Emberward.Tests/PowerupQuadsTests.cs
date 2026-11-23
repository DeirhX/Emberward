using System.Numerics;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>
/// The powerup drops as the entity layer draws them (<see cref="PowerupQuads"/>, DESIGN.md §6.8): the site of the
/// next drop and the drops on the ground whatever the player sees, a ring round a tank with a powerup running only
/// while the tank is in sight, and nothing at all where the rules run no drops.
/// </summary>
public class PowerupQuadsTests
{
    private const int QuadVertices = 6;

    private sealed class Layer
    {
        public required World World { get; init; }
        public PowerupQuads Powerups { get; } = new();
        public EntityMotion Motion { get; } = new();
        private readonly TankRenderState[] _tanks = new TankRenderState[8];

        public void Capture()
        {
            int count = World.TankList.CopyRenderStates(_tanks);
            Motion.CaptureTanks(_tanks.AsSpan(0, count));
            Powerups.CaptureStep(World);
        }

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
                PowerupWorlds.Step(World);
            Capture();
        }

        /// <summary>The whole map in view, one render pixel a cell.</summary>
        public EntityQuads Draw(VisionMap? vision = null, bool ground = true, bool halos = true, bool labels = false)
        {
            var size = World.Terrain.Size;
            var quads = new EntityQuads();
            quads.Begin(new RenderView(size, size, default, 1), phase: 1.25f, alpha: 0f);
            if (ground)
                Powerups.EmitGround(quads);
            if (halos)
                Powerups.EmitHalos(quads, Motion, vision);
            if (labels)
                Powerups.EmitLabels(quads);
            return quads;
        }
    }

    /// <summary>The quads of the powerup shape: where each is centred and the mode in its last parameter.</summary>
    private static List<(Vector2 Centre, float Mode)> Shapes(EntityQuads quads)
    {
        var shapes = new List<(Vector2, float)>();
        for (int i = 0; i < quads.VertexCount; i += QuadVertices)
        {
            if (quads.Vertices[i].Kind != EntityShape.KindPowerup)
                continue;
            Vector2 sum = default;
            for (int v = 0; v < QuadVertices; v++)
                sum += new Vector2(quads.Vertices[i + v].CellX, quads.Vertices[i + v].CellY);
            shapes.Add((sum / QuadVertices, quads.Vertices[i].P3));
        }
        return shapes;
    }

    private static VisionMap SightFrom(World world, Position eye)
    {
        var vision = new VisionMap(world.Terrain);
        vision.Update(world.Terrain, [new VisionSource(eye, 10, 0)], changedCells: []);
        return vision;
    }

    [Fact]
    public void WithoutDrops_NothingIsDrawn()
    {
        var layer = new Layer { World = ArcadeWorlds.Create(ArcadeWorlds.Timed(60)) };
        layer.Step(3);

        Assert.Equal(0, layer.Draw(labels: true).VertexCount);
    }

    [Fact]
    public void TheSiteOfTheNextDrop_IsMarkedFromTheFirstStep_AlsoWhereThePlayerDoesNotSee()
    {
        var layer = new Layer { World = Create() };
        layer.Step();
        PowerupForecast forecast = layer.World.PowerupForecast;
        // An eye in a far corner sees nothing of the site.
        var vision = SightFrom(layer.World, new Position(2, 2));
        Assert.False(Sight.Sees(vision, forecast.Site));

        var (centre, mode) = Assert.Single(Shapes(layer.Draw(vision)));

        Assert.Equal(forecast.Site.X + 0.5f, centre.X, precision: 3);
        Assert.Equal(forecast.Site.Y + 0.5f, centre.Y, precision: 3);
        Assert.Equal(0f, mode);
        Assert.Equal(forecast, layer.Powerups.Forecast);
    }

    [Fact]
    public void TheSitesMarker_TurnsUrgentInItsLastSeconds()
    {
        var layer = new Layer { World = Create() };
        layer.Step(Tweaks.Powerups.FirstDropTicks - DropBoard.UrgentSeconds * Fps - 1);
        Assert.Equal(0f, Assert.Single(Shapes(layer.Draw())).Mode);

        layer.Step(2);

        Assert.Equal(0.5f, Assert.Single(Shapes(layer.Draw())).Mode);
    }

    [Fact]
    public void ADropOnTheGround_IsDrawnBesideTheNextSite_WithItsLabels()
    {
        var layer = new Layer { World = Create() };
        PowerupForecast first = layer.World.PowerupForecast;
        while (layer.World.PowerupDropCount == 0)
            layer.Step();

        var shapes = Shapes(layer.Draw());

        Assert.Equal([0f, 1f], shapes.Select(s => s.Mode));
        Assert.True(Vector2.Distance(shapes[1].Centre, new Vector2(first.Site.X + 0.5f, first.Site.Y + 0.5f)) < 0.01f);
        Assert.Equal(first.Kind, layer.Powerups.Drops[0].Kind);
        // The kind's name, its letter and a countdown at the site and at the drop: more glyphs than shapes.
        Assert.True(layer.Draw(ground: false, halos: false, labels: true).VertexCount > 20 * QuadVertices);
    }

    [Fact]
    public void ATankWithAPowerupRunning_HasARing_OnlyWhileItIsInSight()
    {
        var layer = new Layer { World = Create() };
        var tank = layer.World.TankList.Tanks[1];
        Position site = Take(layer.World, tank, PowerupKind.Bulwark);
        layer.Capture();
        Assert.Equal(1, layer.Powerups.TanksWithEffects);

        var seen = Shapes(layer.Draw(SightFrom(layer.World, site), ground: false));
        var unseen = Shapes(layer.Draw(SightFrom(layer.World, new Position(2, 2)), ground: false));
        var noFog = Shapes(layer.Draw(vision: null, ground: false));

        var (centre, mode) = Assert.Single(seen);
        Assert.Equal(2f, mode);
        Assert.True(Vector2.Distance(centre, new Vector2(site.X + 0.5f, site.Y + 0.5f)) < 2f, $"ring at {centre}, tank at {site}");
        Assert.Empty(unseen);
        Assert.Single(noFog);
    }

    [Fact]
    public void ARing_TurnsUrgentAsThePowerupRunsOut_AndGoesWithIt()
    {
        var layer = new Layer { World = Create() };
        var tank = layer.World.TankList.Tanks[1];
        Take(layer.World, tank, PowerupKind.Overcharge);
        int left = layer.World.PowerupsOf(tank).TicksLeft(PowerupKind.Overcharge);

        layer.Step(left - 2 * Fps);
        Assert.Equal(2.5f, Assert.Single(Shapes(layer.Draw(ground: false))).Mode);

        layer.Step(2 * Fps);
        Assert.Empty(Shapes(layer.Draw(ground: false)));
        Assert.Equal(0, layer.Powerups.TanksWithEffects);
    }

    [Fact]
    public void AnOverhaul_LeavesNoRing()
    {
        var layer = new Layer { World = Create() };
        Take(layer.World, layer.World.TankList.Tanks[0], PowerupKind.Overhaul);
        layer.Capture();

        Assert.Equal(0, layer.Powerups.TanksWithEffects);
        Assert.Empty(Shapes(layer.Draw(ground: false)));
    }

    [Fact]
    public void OnceTheMatchIsOver_NothingIsMarkedAnyMore()
    {
        var world = Create(new MatchRules { TerrainRegrowth = false, Powerups = true, TimeLimitSeconds = 5 });
        var layer = new Layer { World = world };
        layer.Step();
        Assert.NotEmpty(Shapes(layer.Draw()));

        ArcadeWorlds.Kill(world, killer: 1, world.TankList.Tanks[0]);
        ArcadeWorlds.RunClockDownTo(world, 0);
        layer.Step();

        Assert.True(world.IsMatchOver);
        Assert.Equal(0, layer.Draw(labels: true).VertexCount);
    }
}
