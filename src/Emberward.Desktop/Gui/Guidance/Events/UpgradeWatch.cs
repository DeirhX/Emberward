namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;

/// <summary>Base upgrades bought, and upgrades the player tried to buy and could not.</summary>
internal sealed class UpgradeWatch : IEventWatch
{
    private readonly int[] _levels = new int[BaseUpgrades.Count];
    private Tally _refused;

    public void Reset() { }

    public void OnStepEvent(World world, Tank player, in StepEvent e) { }

    public void Update(in GuidanceFrame f)
    {
        for (int i = 0; i < BaseUpgrades.Count; i++)
        {
            int level = f.Home.Upgrades.Level((BaseUpgrade)i);
            if (f.Primed && level > _levels[i])
            {
                var (name, effect) = BaseUpgrades.Describe((BaseUpgrade)i);
                f.Feed.Post(f.Now, $"upgrade-{i}-{level}", $"{name} upgraded to level {level}", effect, NotificationKind.Good, cooldown: 0f);
            }
            _levels[i] = level;
        }

        if (_refused.Change(f.Player.UpgradeRefusedTicks) > 0 && f.Primed)
            f.Feed.Post(f.Now, "upgrade-refused", "Can't buy that upgrade", f.Player.LastUpgradeRefusal switch
            {
                UpgradeRefusal.NotDocked => "Upgrades are bought while docked at your base.",
                UpgradeRefusal.Maxed => "That upgrade is already at its top level.",
                _ => "Not enough ore in the base store. Bring ore to the core or a relay, or let harvesters mine it.",
            }, NotificationKind.Warning, cooldown: 2f);
    }
}
