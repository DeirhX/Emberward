namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Types;
using Emberward.Core.Network;

/// <summary>
/// The player's network coverage (DESIGN.md §4.6): a faint field around every node, and while the tank is covered
/// (outside a base) a cool tether from the node carrying its heat away, and while its hold unloads at a relay a stream
/// of ore crystals to it (at the core, <see cref="DepositQuads"/> pours them in). Off network the fields brighten a little, so the way back reads at a glance.
/// </summary>
public sealed class CoverageQuads
{
    private readonly List<CoverageNode> _coverageNodes = new();
    private TankCoverage _playerCoverage;
    private bool _playerOffNetwork;
    private bool _playerUnloading;
    private Position _unloadNode;

    public void Capture(World world, int localSlot)
    {
        _coverageNodes.Clear();
        _playerCoverage = TankCoverage.None;
        _playerOffNetwork = false;
        _playerUnloading = false;
        if (localSlot >= world.TankList.Count || world.TankList.Tanks[localSlot] is not { Base: { } home } player)
            return;
        world.CollectCoverageNodes(home, _coverageNodes);
        _playerCoverage = player.IsDead ? TankCoverage.None : player.Coverage;
        _playerOffNetwork = !player.IsDead && player.IsOffNetwork;
        // At the core the deposit pour (DepositQuads) shows the ore going in; the beam stays for relays.
        _playerUnloading = player.IsUnloadingOre && !player.IsDead && player.UnloadNode != home.Position;
        _unloadNode = player.UnloadNode;
    }

    /// <summary>The coverage fields around every node.</summary>
    public void Emit(EntityQuads quads)
    {
        float scale = quads.View.PixelScale;
        float phase = quads.Phase;
        float emphasis = _playerOffNetwork ? 1f : 0f;
        foreach (var node in _coverageNodes)
        {
            float r = node.Radius;
            if (quads.IsOffScreen(quads.ScreenX(node.Position.X), quads.ScreenY(node.Position.Y), (r + 4f) * scale))
                continue;
            float nx = EntityQuads.Centre(node.Position.X);
            float ny = EntityQuads.Centre(node.Position.Y);
            quads.Add(nx, ny, 1f, 0f, -r - 4f, r + 4f, -r - 4f, r + 4f, Tweaks.Colors.Relay, EntityShape.KindCoverage,
                r, node.Strength, phase, emphasis);
        }

    }

    /// <summary>
    /// The tether, and while the hold unloads at a relay the ore stream to the relay the tank touches
    /// (<see cref="Tank.UnloadNode"/>), handed to <paramref name="beams"/>; <paramref name="player"/> is the interpolated
    /// player tank, where they start.
    /// </summary>
    public void SubmitTether(BeamFader beams, PositionF player)
    {
        if (_playerUnloading)
            Submit(beams, EntityShape.KindOreBeam, player, _unloadNode, 1f, 1.5f);
        if (_playerCoverage.Covered)
            // From the tank toward the node: heat flows that way. Keyed by the node, so it follows the tank about.
            Submit(beams, EntityShape.KindTether, player, _playerCoverage.Node, _playerCoverage.Strength, 2.5f);
    }

    private static void Submit(BeamFader beams, float kind, PositionF player, Position node, float strength, float width)
    {
        float dx = player.X - node.X, dy = player.Y - node.Y;
        if (MathF.Sqrt(dx * dx + dy * dy) < 4f)
            return;   // standing on the node: nothing to draw
        beams.Submit(new BeamKey(kind, node, node), player.X, player.Y, node.X, node.Y, strength, width);
    }
}
