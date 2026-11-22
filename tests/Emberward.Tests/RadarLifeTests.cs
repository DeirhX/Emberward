using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// Life echoes on the radar (the white-violet blips): something alive returns one blip at its centre, not a line of
/// blips along the face of the square that stands for it; a blip of something that has moved goes out when the next
/// pulse passes it; and emberites out of their nests return life echoes where they are.
/// </summary>
public class RadarLifeTests
{
    private static readonly Position Origin = new(200, 200);

    private static TerrainGrid Dirt()
    {
        var terrain = new TerrainGrid(new Size(401, 401));
        terrain.Fill(TerrainPixel.Dirt);
        return terrain;
    }

    private static void Pulse(Radar radar, TerrainGrid terrain, RadarTarget[] targets)
    {
        for (int i = 0; i < Tweaks.Radar.PulsePeriodTicks; i++)
            radar.Advance(terrain, terrain, Origin, ownColor: 0, targets);
    }

    private static List<RadarContact> Life(Radar radar)
    {
        var contacts = new List<RadarContact>();
        for (int i = 0; i < radar.ContactCount; i++)
            if (radar.ContactAt(i).Echo == RadarEcho.Life)
                contacts.Add(radar.ContactAt(i));
        return contacts;
    }

    [Theory]
    [InlineData(240, 200)]
    [InlineData(230, 230)]
    [InlineData(170, 185)]
    public void AWideLivingTarget_ReturnsOneBlipAtItsCentre_NotALineAlongItsFace(int x, int y)
    {
        // A nest's square is many ray spacings wide: every ray that met its face used to leave a blip there.
        var terrain = Dirt();
        var radar = new Radar(new VisionMap(terrain));
        var centre = new Position(x, y);

        Pulse(radar, terrain, [new RadarTarget(centre, 8, Alive: true)]);

        var life = Life(radar);
        var blip = Assert.Single(life);
        Assert.Equal(centre, blip.Position);
    }

    [Fact]
    public void ALivingTargetThatMoves_LeavesNoStaleBlip()
    {
        var terrain = Dirt();
        var radar = new Radar(new VisionMap(terrain));
        Pulse(radar, terrain, [new RadarTarget(new Position(240, 200), 6, Alive: true)]);
        Assert.Equal(new Position(240, 200), Assert.Single(Life(radar)).Position);

        // It has moved a few cells, still covering its old centre, which lies behind the face the next rays stop at.
        Pulse(radar, terrain, [new RadarTarget(new Position(243, 200), 6, Alive: true)]);

        Assert.Equal(new Position(243, 200), Assert.Single(Life(radar)).Position);
    }

    [Fact]
    public void ALivingBlipInARocksShadow_Stays_EvenIfWhatLeftItHasMoved()
    {
        var terrain = Dirt();
        var radar = new Radar(new VisionMap(terrain));
        Pulse(radar, terrain, [new RadarTarget(new Position(240, 200), 3, Alive: true)]);
        Assert.Equal(new Position(240, 200), Assert.Single(Life(radar)).Position);

        // Rock now hides it, and it moves on behind the rock: the next pulse cannot reach the blip, so it learns nothing
        // of the move and the blip stays until it fades.
        for (int y = 180; y <= 225; y++)
            for (int x = 220; x <= 222; x++)
                terrain.SetPixelRaw(new Position(x, y), TerrainPixel.Rock);
        Pulse(radar, terrain, [new RadarTarget(new Position(240, 212), 3, Alive: true)]);

        Assert.Equal(new Position(240, 200), Assert.Single(Life(radar)).Position);
    }

    [Fact]
    public void ALivingTargetThatStays_KeepsItsBlip_AndNoSecond()
    {
        var terrain = Dirt();
        var radar = new Radar(new VisionMap(terrain));
        RadarTarget[] targets = [new RadarTarget(new Position(200, 245), 3, Alive: true)];

        for (int pulse = 0; pulse < 3; pulse++)
        {
            Pulse(radar, terrain, targets);
            Assert.Equal(new Position(200, 245), Assert.Single(Life(radar)).Position);
        }
    }

    [Fact]
    public void EmberitesOut_ReturnLifeEchoesWhereTheyAre()
    {
        // The hunted tank stands in an open arena with a nest beside it; the other tank watches from the dirt below,
        // out of sight of the creatures but within the radar's life range.
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        var centre = new Position(60, 100);
        for (int y = -30; y <= 30; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(centre + new Offset(x, y), TerrainPixel.Blank);
        var hunted = world.TankList.Tanks[0];
        var watcher = world.TankList.Tanks[1];
        hunted.Position = centre;
        watcher.Position = new Position(75, 150);
        var nestCentre = centre + new Offset(30, 0);
        var nest = world.Emberites!.AddNest(nestCentre, radius: 4, creatures: 10);
        // The radar starts as the first creatures come out (a nest's lull is as long as a pulse period, so a radar
        // started with the stir would send every pulse past the arena before each group is out).
        for (int i = 0; i < Tweaks.Emberites.StirTicks + 4 * Tweaks.Emberites.PulseSpacingTicks; i++)
        {
            hunted.Heat = 60f;
            world.Advance(_ => default);
        }
        var radar = world.TrackRadar(watcher);

        // Radar runs last in a step, after the creatures have moved: a blip born this tick sits on a creature now.
        int found = 0;
        bool anyOut = false;
        for (int i = 0; i < 3 * Tweaks.Radar.PulsePeriodTicks; i++)
        {
            hunted.Heat = 60f;
            world.Advance(_ => default);
            var states = new EmberiteRenderState[world.Emberites.OutCount];
            world.CopyEmberiteRenderStates(states);
            anyOut |= states.Length > 0;
            foreach (var blip in Life(radar).Where(c => c.BornTick == radar.Tick && c.Position != nestCentre))
            {
                Assert.Contains(states, s => Position.DistanceSquared((Position)s.Position, blip.Position) <= 1);
                found++;
            }
        }
        Assert.True(nest.Awake && anyOut, "the nest should have let creatures out");
        Assert.True(found > 0, "no creature returned a life echo");
    }
}
