namespace Emberward.Tests;

using Emberward.Core.Types;
using Emberward.Core.Bots;
using Emberward.Core;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Match;
using Emberward.Core.Terrain;

public class BotEnemyChoiceTests
{
    [Fact]
    public void WithTwoTanks_EachPicksTheOther()
    {
        var tanks = TestHelpers.CreateSeededWorld(new Size(320, 200)).TankList.Tanks;
        Assert.Equal(2, tanks.Count);

        Assert.Same(tanks[0], BotTankAI.PickEnemy(tanks, self: 1));
        Assert.Same(tanks[1], BotTankAI.PickEnemy(tanks, self: 0));
    }

    [Fact]
    public void ALoneTank_HasNoEnemy()
    {
        var tanks = TestHelpers.CreateSeededWorld(new Size(320, 200)).TankList.Tanks;

        Assert.Null(BotTankAI.PickEnemy([tanks[0]], self: 0));
    }
    [Fact]
    public void AnEnemyAtItsBase_IsPickedBeforeANearerOneDockedAtHome()
    {
        // Three bases in a row; the bot is tank 2 on the right, tank 1 docked in the middle base, nearest it.
        var size = new Size(220, 72);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { TerrainRegrowth = false });
        world.Initialize(ground, [new Position(25, 36), new Position(110, 36), new Position(195, 36)]);
        var tanks = world.TankList.Tanks;
        Assert.Same(tanks[1], BotTankAI.PickEnemy(tanks, self: 2));

        // Tank 0 comes round to the bot's base, farther from the bot than tank 1 but at its walls: it is the one fought.
        tanks[2].Position = tanks[2].Base!.Position + new Offset(-BaseLayout.Half - 30, 0);
        tanks[0].Position = tanks[2].Base!.Position + new Offset(0, BaseLayout.Half + 8);
        Assert.True(Position.DistanceSquared(tanks[0].Position, tanks[2].Position) >
            Position.DistanceSquared(tanks[1].Position, tanks[2].Position));
        Assert.Same(tanks[0], BotTankAI.PickEnemy(tanks, self: 2));
    }
}
