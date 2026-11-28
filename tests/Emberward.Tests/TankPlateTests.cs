using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Desktop.Config;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>
/// The name plates over enemy tanks (<see cref="TankPlateQuads"/>): an enemy tank in the player's sight has a plate
/// that follows it; out of sight the plate stays where it was last seen and fades out over
/// <see cref="TankPlateQuads.FadeSteps"/>; seen again elsewhere the plate follows it again at once; a dead tank's plate
/// goes, and the player's own tank never has one.
/// </summary>
public class TankPlateTests
{
    // Clear of both bases of the 320 x 200 test map, which stand at (130, 21) and (185, 178).
    private static readonly Position Centre = new(60, 100);

    /// <summary>In the player's open pocket, clear of its tank.</summary>
    private static readonly Position InSight = Centre + new Offset(20, 0);

    /// <summary>Elsewhere in the pocket.</summary>
    private static readonly Position InSightElsewhere = Centre + new Offset(-20, 0);

    /// <summary>Deep in the dirt around the pocket, out of the player's sight.</summary>
    private static readonly Position OutOfSight = Centre + new Offset(0, 24);

    /// <summary>A world with the player's vision tracked, its tank in an open pocket of dirt and the enemy in sight.</summary>
    private static (World World, VisionMap Vision, Tank Enemy) Arena()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false);
        for (int y = -32; y <= 32; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(Centre + new Offset(x, y), Math.Abs(x) <= 32 && Math.Abs(y) <= 10 ? TerrainPixel.Blank : TerrainPixel.Dirt);
        var player = world.TankList.Tanks[0];
        player.Position = Centre;
        var enemy = world.TankList.Tanks[1];
        enemy.Position = InSight;
        var vision = world.TrackVision(player);
        return (world, vision, enemy);
    }

    private static void Step(World world, VisionMap vision, TankPlateQuads plates, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            world.Advance(_ => default);
            plates.CaptureStep(world, localSlot: 0, vision);
        }
    }

    [Fact]
    public void AnEnemyInSight_HasAPlateThatFollowsIt_AndThePlayersOwnTankHasNone()
    {
        var (world, vision, enemy) = Arena();
        var plates = new TankPlateQuads();
        Step(world, vision, plates);
        Assert.True(vision.IsVisible(enemy.Position));

        Assert.False(plates.Plates[0].Active);
        TankPlateQuads.Plate plate = plates.Plates[1];
        Assert.True(plate.Active);
        Assert.True(plate.InSight);
        Assert.Equal(enemy.Position, plate.Cell);
        Assert.Equal(enemy.Color, plate.Color);
        Assert.Equal("PLAYER 2", plate.Name);
        Assert.Equal(1f, TankPlateQuads.Opacity(plate, 0.5f));

        world.SetBotControlled(1, botControlled: true);
        Step(world, vision, plates);
        Assert.Equal("BOT 2", plates.Plates[1].Name);
    }

    [Fact]
    public void ANameGivenAfterThePlateIsMade_ReachesItAtOnce()
    {
        var (world, vision, _) = Arena();
        var plates = new TankPlateQuads();
        Step(world, vision, plates);
        Assert.Equal("PLAYER 2", plates.Plates[1].Name);

        plates.SetPlayerName(1, "Ann");

        Assert.Equal("ANN", plates.Plates[1].Name);
    }

    [Fact]
    public void APlayerName_IsShownInCapitals_UnlessABotDrivesTheSeat()
    {
        var (world, vision, _) = Arena();
        var plates = new TankPlateQuads();
        plates.SetPlayerName(1, "Ann (2)");
        Step(world, vision, plates);
        Assert.Equal("ANN (2)", plates.Plates[1].Name);

        plates.SetPlayerName(1, "a name far too long for a plate");
        Step(world, vision, plates);
        Assert.Equal(TankPlateQuads.MaxNameLength, plates.Plates[1].Name.Length);

        world.SetBotControlled(1, botControlled: true);
        Step(world, vision, plates);
        Assert.Equal("BOT 2", plates.Plates[1].Name);
    }

    [Fact]
    public void ALostEnemy_LeavesItsPlateWhereItWasLastSeen_FadingOut_ThenGone()
    {
        var (world, vision, enemy) = Arena();
        var plates = new TankPlateQuads();
        Step(world, vision, plates);
        Position lastSeen = enemy.Position;

        enemy.Position = OutOfSight;
        Step(world, vision, plates);
        Assert.False(vision.IsVisible(enemy.Position));
        TankPlateQuads.Plate plate = plates.Plates[1];
        Assert.True(plate.Active);
        Assert.False(plate.InSight);
        Assert.Equal(lastSeen, plate.Cell);
        Assert.Equal(DesktopScreenTweaks.TankPlateGhostOpacity, TankPlateQuads.Opacity(plate, 0f), 3);

        float before = TankPlateQuads.Opacity(plate, 1f);
        Step(world, vision, plates, TankPlateQuads.FadeSteps / 2);
        plate = plates.Plates[1];
        Assert.True(plate.Active);
        Assert.Equal(lastSeen, plate.Cell);
        float halfway = TankPlateQuads.Opacity(plate, 0f);
        Assert.InRange(halfway, 0.3f * DesktopScreenTweaks.TankPlateGhostOpacity, 0.7f * DesktopScreenTweaks.TankPlateGhostOpacity);
        Assert.True(halfway < before);

        Step(world, vision, plates, TankPlateQuads.FadeSteps);
        Assert.False(plates.Plates[1].Active);
        Assert.Equal(0f, TankPlateQuads.Opacity(plates.Plates[1], 0.5f));
    }

    [Fact]
    public void TheFade_TakesTheConfiguredSeconds()
    {
        float seconds = TankPlateQuads.FadeSteps / (float)Emberward.Core.Config.Tweaks.Perf.TargetFps;
        Assert.Equal(DesktopScreenTweaks.TankPlateFadeSeconds, seconds, 1);
        Assert.InRange(DesktopScreenTweaks.TankPlateFadeSeconds, 3f, 4f);
    }

    [Fact]
    public void ALostEnemy_SeenAgainElsewhere_DropsTheOldPlate_AndIsFollowedAgain()
    {
        var (world, vision, enemy) = Arena();
        var plates = new TankPlateQuads();
        Step(world, vision, plates);
        enemy.Position = OutOfSight;
        Step(world, vision, plates, 10);
        Assert.False(plates.Plates[1].InSight);

        enemy.Position = InSightElsewhere;
        Step(world, vision, plates);
        TankPlateQuads.Plate plate = plates.Plates[1];
        Assert.True(plate.InSight);
        Assert.Equal(0, plate.StepsLost);
        Assert.Equal(enemy.Position, plate.Cell);
        Assert.Equal(1f, TankPlateQuads.Opacity(plate, 0f));
    }

    [Fact]
    public void ADeadEnemy_LosesItsPlate_InSightOrNot()
    {
        var (world, vision, enemy) = Arena();
        var plates = new TankPlateQuads();
        Step(world, vision, plates);
        enemy.Die();
        Step(world, vision, plates);
        Assert.False(plates.Plates[1].Active);

        // A ghost goes too.
        var (world2, vision2, enemy2) = Arena();
        var plates2 = new TankPlateQuads();
        Step(world2, vision2, plates2);
        enemy2.Position = OutOfSight;
        Step(world2, vision2, plates2);
        Assert.True(plates2.Plates[1].Active);
        enemy2.Die();
        Step(world2, vision2, plates2);
        Assert.False(plates2.Plates[1].Active);
    }

    [Fact]
    public void ATank_IsNoRivalOfItself_AndTanksOfTwoBasesAreRivals()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false);
        Tank a = world.TankList.Tanks[0], b = world.TankList.Tanks[1];
        Assert.False(a.IsRivalOf(a));
        Assert.True(a.IsRivalOf(b));
        Assert.True(b.IsRivalOf(a));
    }

    [Fact]
    public void ThePlate_IsDrawnInTheTanksColour_AndTheGhostStandsStillAboveWhereItWasSeen()
    {
        var (world, vision, enemy) = Arena();
        var layer = new EntityLayerBuilder();
        world.Advance(_ => default);
        layer.CaptureStep(world, 0, vision);
        Color team = TankSprites.GetPixelColor(1, enemy.Color);

        List<EntityVertex> live = Labels(world, vision, layer, labelCellSize: 6f, team);
        Assert.NotEmpty(live);
        // Hidden while the caller turns the plates off (zoomed far out).
        Assert.Empty(Labels(world, vision, layer, labelCellSize: 0f, team));

        enemy.Position = OutOfSight;
        world.Advance(_ => default);
        layer.CaptureStep(world, 0, vision);
        List<EntityVertex> ghost = Labels(world, vision, layer, labelCellSize: 6f, team);
        Assert.NotEmpty(ghost);
        Assert.All(ghost, v => Assert.Equal((0f, 0f), (v.MotionX, v.MotionY)));
        Assert.All(ghost, v => Assert.True(v.CellY < InSight.Y));
    }

    [Fact]
    public void ANameGivenWhileThePlateIsAGhost_ReachesTheGhostAtOnce()
    {
        var (world, vision, enemy) = Arena();
        var plates = new TankPlateQuads();
        Step(world, vision, plates);
        enemy.Position = OutOfSight;
        Step(world, vision, plates);
        Assert.True(plates.Plates[1].Active);
        Assert.False(plates.Plates[1].InSight);

        plates.SetPlayerName(1, "Bob");

        Assert.Equal("BOB", plates.Plates[1].Name);
        Assert.False(plates.Plates[1].InSight);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void WithNoTankOfItsOwn_TheScreenLabelsEveryLivingTank(int localSlot)
    {
        var (world, _, enemy) = Arena();
        var plates = new TankPlateQuads();
        plates.SetPlayerName(0, "Ann");
        world.Advance(_ => default);

        // A spectator has no vision of its own: no fog, every tank is seen.
        plates.CaptureStep(world, localSlot, vision: null);

        Assert.Equal(world.TankList.Count, CountActive(plates));
        Assert.Equal("ANN", plates.Plates[0].Name);
        Assert.Equal("PLAYER 2", plates.Plates[1].Name);
        Assert.True(plates.Plates[1].InSight);

        enemy.Die();
        world.Advance(_ => default);
        plates.CaptureStep(world, localSlot, vision: null);
        Assert.False(plates.Plates[1].Active);
        Assert.True(plates.Plates[0].Active);
    }

    [Fact]
    public void ZoomedOut_ThePlatesStayFullyDrawn_AndTheirLetteringStopsShrinking()
    {
        // At the usual zoom (6 points a cell, 6 render pixels a cell) a dot is a quarter cell.
        Assert.Equal(TankPlateQuads.DotCells, TankPlateQuads.DotSize(6f, 6f), 4);
        // Zoomed out it holds the minimum on screen: one point a dot, 7 points of lettering.
        Assert.Equal(DesktopScreenTweaks.TankPlateMinDotPoints / 2f, TankPlateQuads.DotSize(2f, 6f), 4);
        Assert.Equal(DesktopScreenTweaks.TankPlateMinDotPoints / 0.5f, TankPlateQuads.DotSize(0.5f, 6f), 4);
        Assert.Equal(DesktopScreenTweaks.TankPlateMinDotPoints, TankPlateQuads.DotSize(1f, 6f), 4);
        // Never under one render pixel.
        Assert.Equal(0.5f, TankPlateQuads.DotSize(6f, 2f), 4);

        var (world, vision, enemy) = Arena();
        var layer = new EntityLayerBuilder();
        world.Advance(_ => default);
        layer.CaptureStep(world, 0, vision);
        Color team = TankSprites.GetPixelColor(1, enemy.Color);
        // Below 2.5 points a cell the plates used to fade out entirely, as the charge labels do.
        List<EntityVertex> far = Labels(world, vision, layer, labelCellSize: 1.5f, team, pixelScale: 6);
        Assert.NotEmpty(far);
        Assert.Equal(1f, far.Max(v => v.P3), 3);
        float farHeight = Height(far);
        float nearHeight = Height(Labels(world, vision, layer, labelCellSize: 6f, team, pixelScale: 6));
        Assert.True(farHeight > nearHeight * 2f, $"zoomed out {farHeight} cells, usual {nearHeight} cells");

        static float Height(List<EntityVertex> vertices) => vertices.Max(v => v.CellY) - vertices.Min(v => v.CellY);
    }

    [Fact]
    public void ALostPlate_StaysWhereItWasLastDrawn_WithNoJump()
    {
        var (world, vision, enemy) = Arena();
        var layer = new EntityLayerBuilder();
        Color team = TankSprites.GetPixelColor(1, enemy.Color);
        // Driving right a cell a step, the tank (and its plate) is drawn between steps and led ahead of its cell.
        for (int i = 0; i < 4; i++)
        {
            enemy.Position += new Offset(1, 0);
            world.Advance(_ => default);
            layer.CaptureStep(world, 0, vision);
        }
        Assert.True(layer.TankPlates.Plates[1].InSight);
        const float Alpha = 0.6f;
        (float X, float Y) drawn = DrawnCorner(Labels(world, vision, layer, labelCellSize: 6f, team, Alpha), Alpha);
        Position lastSeen = enemy.Position;

        // A wall goes up between the player and the tank as it drives on a cell: it is lost.
        for (int y = -12; y <= 12; y++)
            for (int x = -2; x <= 2; x++)
                world.Terrain.SetPixel(Centre + new Offset(10 + x, y), TerrainPixel.Rock);
        enemy.Position += new Offset(1, 0);
        world.Advance(_ => default);
        layer.CaptureStep(world, 0, vision);
        Assert.False(vision.IsVisible(enemy.Position));
        Assert.False(layer.TankPlates.Plates[1].InSight);
        Assert.Equal(lastSeen, layer.TankPlates.Plates[1].Cell);

        (float X, float Y) ghost = DrawnCorner(Labels(world, vision, layer, labelCellSize: 6f, team, 0f), 0f);
        Assert.Equal(drawn.X, ghost.X, 3);
        Assert.Equal(drawn.Y, ghost.Y, 3);
        // Standing at the last-seen cell instead would have moved it by a good part of a cell.
        var anchor = layer.TankPlates.Plates[1].Anchor;
        Assert.True(MathF.Abs(anchor.X - lastSeen.X) > 0.25f, $"drawn at {anchor.X}, seen at {lastSeen.X}");
    }

    [Fact]
    public void ATankLostByATeleport_LeavesItsPlateAtTheLastSeenCell_NotWhereItWent()
    {
        var (world, vision, enemy) = Arena();
        var layer = new EntityLayerBuilder();
        for (int i = 0; i < 3; i++)
        {
            world.Advance(_ => default);
            layer.CaptureStep(world, 0, vision);
        }
        Position lastSeen = enemy.Position;

        enemy.Position = OutOfSight;
        world.Advance(_ => default);
        layer.CaptureStep(world, 0, vision);

        TankPlateQuads.Plate plate = layer.TankPlates.Plates[1];
        Assert.True(plate.Active);
        Assert.False(plate.InSight);
        Assert.Equal((PositionF)lastSeen, plate.Anchor);
    }

    private static int CountActive(TankPlateQuads plates)
    {
        int active = 0;
        foreach (TankPlateQuads.Plate plate in plates.Plates)
            active += plate.Active ? 1 : 0;
        return active;
    }

    /// <summary>The top-left corner of the quads as the shader draws them at <paramref name="alpha"/>.</summary>
    private static (float X, float Y) DrawnCorner(List<EntityVertex> vertices, float alpha)
    {
        Assert.NotEmpty(vertices);
        return (vertices.Min(v => v.CellX + v.MotionX * alpha), vertices.Min(v => v.CellY + v.MotionY * alpha));
    }

    /// <summary>The glyph and plate quads in <paramref name="team"/>'s colour the layer draws over the whole map, a cell <paramref name="pixelScale"/> render pixels.</summary>
    private static List<EntityVertex> Labels(
        World world, VisionMap vision, EntityLayerBuilder layer, float labelCellSize, Color team, float alpha = 0.5f,
        int pixelScale = 1)
    {
        var size = world.Terrain.Size;
        layer.Build(new RenderView(new Size(size.X * pixelScale, size.Y * pixelScale), size, default, pixelScale), alpha, 0f, snapHeadings: true, visualSeconds: 0f, vision,
            labelCellSize: labelCellSize);
        var found = new List<EntityVertex>();
        for (int v = 0; v < layer.VertexCount; v++)
        {
            EntityVertex vertex = layer.Vertices[v];
            if (vertex.Kind == EntityShape.KindPowerLabel &&
                MathF.Abs(vertex.R - team.R / 255f) < 1e-4f && MathF.Abs(vertex.G - team.G / 255f) < 1e-4f &&
                MathF.Abs(vertex.B - team.B / 255f) < 1e-4f)
                found.Add(vertex);
        }
        return found;
    }
}
