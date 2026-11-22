using Emberward.Core;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>What the entity layer draws for a shot in flight (<see cref="ProjectileQuads"/>).</summary>
public class ProjectileQuadsTests
{
    /// <summary>
    /// The hot shot's softness (params.z): a death burst's shrapnel is a soft ember (1), while bullets and gatling rounds
    /// keep their crisp heads (0).
    /// </summary>
    [Theory]
    [InlineData(ProjectileType.Shrapnel, 1f)]
    [InlineData(ProjectileType.Bullet, 0f)]
    [InlineData(ProjectileType.Round, 0f)]
    public void AShot_HasItsSoftness(ProjectileType type, float softness)
    {
        var size = new Size(100, 72);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { TerrainRegrowth = false });
        world.Initialize(ground, [new Position(25, 36), new Position(75, 36)]);
        var at = new Position(50, 20);
        var speed = new VectorF(1, 0);
        world.Projectiles.Add(type switch
        {
            ProjectileType.Shrapnel => Projectile.CreateShrapnel(at, speed, life: 10),
            ProjectileType.Round => Projectile.CreateRound(at, speed, ownerColor: 0, damage: 1, life: 10, pierce: 0),
            _ => Projectile.CreateBullet(at, speed, ownerColor: 0),
        });
        world.Advance(_ => default);

        var shots = new ProjectileQuads();
        shots.Capture(world);
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), 1.3f, 0f);
        shots.Emit(quads, vision: null);

        Assert.Equal(6, quads.VertexCount);
        Assert.Equal(EntityShape.KindHotShot, quads.Vertices[0].Kind);
        Assert.Equal(softness, quads.Vertices[0].P2);
    }
}
