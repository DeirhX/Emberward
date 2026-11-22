namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>Enemy fire on the player's base: its gate shield, its core, its destruction.</summary>
internal sealed class BaseAttackWatch : IEventWatch
{
    private int _hits;
    private bool _shieldBroken;
    private bool _coreHit;
    private bool _destroyed;

    /// <summary>When the player's base last took enemy fire, in HUD seconds; far negative before the first hit.</summary>
    public float LastHitTime { get; private set; } = -1e9f;

    /// <summary>Where the last enemy bullet struck the player's base.</summary>
    public Position LastHitAt { get; private set; }

    public void Reset()
    {
        _hits = 0;
        _destroyed = false;
        _shieldBroken = false;
        _coreHit = false;
        LastHitTime = -1e9f;
    }

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Color != player.Color || e.Kind is not (StepEventKind.BaseDamaged or StepEventKind.BaseDestroyed
            or StepEventKind.BaseShieldHit or StepEventKind.BaseShieldBroken))
            return;
        _hits++;
        _shieldBroken |= e.Kind == StepEventKind.BaseShieldBroken;
        _coreHit |= e.Kind is StepEventKind.BaseDamaged or StepEventKind.BaseDestroyed;
        LastHitAt = e.Position;
        _destroyed |= e.Kind == StepEventKind.BaseDestroyed;
    }

    public void Update(in GuidanceFrame f)
    {
        if (_hits == 0)
            return;
        LastHitTime = f.Now;
        if (_destroyed)
            f.Feed.Post(f.Now, "base-destroyed", "Your base was destroyed",
                "Without a base your tank cannot respawn.", NotificationKind.Danger, cooldown: 1e9f);
        else
            // The gates-open warning has its own key, so it is not held back by the shield-taking one.
            f.Feed.Post(f.Now, _shieldBroken ? "base-gates-open" : "base-attacked", "Your base is under attack",
                _shieldBroken
                    ? "Its gate shield is spent and the gates stand open. Drive off the attacker and keep the base charged."
                    : _coreHit
                        ? "Shells are reaching its core. Drive off the attacker."
                        : "Its gate shield is taking the hits and drains as it does. Defend it, and keep it charged.",
                NotificationKind.Danger, cooldown: 12f);
        _hits = 0;
        _shieldBroken = false;
        _coreHit = false;
    }
}
