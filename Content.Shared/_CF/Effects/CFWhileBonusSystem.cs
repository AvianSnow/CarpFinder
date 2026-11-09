using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Items;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Puts bonuses on a creature while it is raging, has panache, or wears no armor, and takes them off when it stops. It looks a
/// few times a second instead of being told, so nothing that starts or ends one of those states has to know about it.
/// </summary>
public sealed partial class CFWhileBonusSystem : EntitySystem
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.25);
    private static readonly CFBonusWhile[] States = Enum.GetValues<CFBonusWhile>();

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFImplementSystem _implements = default!;
    [Dependency] private CFTacticSystem _tactics = default!;

    public void Add(EntityUid uid, CFWhileBonus bonus)
    {
        var comp = EnsureComp<CFWhileBonusesComponent>(uid);
        comp.Bonuses.Add(bonus);

        // A state already in effect has to be put on again to include this one.
        if (comp.Active.Remove(bonus.While))
            _bonuses.RemoveBonusesFrom(uid, Source(bonus.While));

        Refresh(uid, comp);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // The interval is on the component so that nothing is kept on the system.
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFWhileBonusesComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextCheck)
                continue;

            comp.NextCheck = now + CheckEvery;
            Refresh(uid, comp);
        }
    }

    private void Refresh(EntityUid uid, CFWhileBonusesComponent comp)
    {
        foreach (var state in States)
        {
            var on = IsIn(uid, state);
            if (on == comp.Active.Contains(state))
                continue;

            if (!on)
            {
                comp.Active.Remove(state);
                _bonuses.RemoveBonusesFrom(uid, Source(state));
                continue;
            }

            comp.Active.Add(state);
            foreach (var bonus in comp.Bonuses)
            {
                if (bonus.While == state)
                    _bonuses.AddPermanentBonus(uid, bonus.Stat, bonus.Type, bonus.Value, bonus.Trait, bonus.Skill, Source(state));
            }
        }
    }

    private bool IsIn(EntityUid uid, CFBonusWhile state)
    {
        return state switch
        {
            CFBonusWhile.Raging => CompOrNull<CFCombatOptionsComponent>(uid)?.Raging == true,
            CFBonusWhile.Panache => CompOrNull<CFCombatOptionsComponent>(uid)?.Panache == true,
            CFBonusWhile.Unarmored => !_armor.TryGetWornArmor(uid, out var armor) || armor.Comp.Category == CFArmorCategory.Unarmored,
            CFBonusWhile.HoldingRegalia => _implements.IsHolding(uid, CFImplementKind.Regalia),
            CFBonusWhile.HoldingLantern => _implements.IsHolding(uid, CFImplementKind.Lantern),
            CFBonusWhile.HasBanner => _tactics.HasBanner(uid),
            _ => false,
        };
    }

    private static string Source(CFBonusWhile state)
    {
        return $"while:{state}";
    }
}
