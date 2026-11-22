using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>Creatures that gave up a tank for docking take it up again when it comes out in their sight.</summary>
public class EmberiteResumeTests
{
    private static int Bites(World world, int ticks, float heat, Action? each = null)
    {
        int bites = 0;
        for (int i = 0; i < ticks; i++)
        {
            world.TankList.Tanks[0].Heat = heat;
            each?.Invoke();
            world.Advance(_ => default);
            foreach (var e in world.StepEvents.ToArray())
                if (e.Kind == StepEventKind.EmberiteBit)
                    bites++;
        }
        return bites;
    }

    [Fact]
    public void ACreatureThatSawTheTankUndock_AttacksAgain()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        var tank = world.TankList.Tanks[0];
        var home = tank.Base!;
        int toward = home.Position.Y < world.Terrain.Height / 2 ? 1 : -1;
        for (int d = BaseLayout.Half + 1; d <= BaseLayout.Half + 40; d++)
            for (int x = -14; x <= 14; x++)
                world.Terrain.SetPixel(home.Position + new Offset(x, toward * d), TerrainPixel.Blank);
        world.Emberites!.AddNest(home.Position + new Offset(0, toward * (BaseLayout.Half + 22)), radius: 4, creatures: 6);

        var outside = home.Position + new Offset(0, toward * (BaseLayout.Half + 5));
        var inside = home.Position + new Offset(0, toward * BaseLayout.Lane);
        tank.Position = outside;
        Bites(world, 120, heat: 80f);
        tank.Position = inside;
        Assert.True(tank.IsDockedAtOwnBase);
        Bites(world, 60, heat: 80f);

        tank.Position = outside;
        Assert.False(tank.IsDockedAtOwnBase);
        Assert.True(Bites(world, 240, heat: 80f) > 0, "creatures that see the tank come out should attack again");
    }
}
