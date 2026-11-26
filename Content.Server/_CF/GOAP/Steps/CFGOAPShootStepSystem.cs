using Content.Server.CF.GOAP.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Stands where it is and lets the GOAP ranged loop (<see cref="CFGOAPCombatSystem"/>) loose shots, which CF turns into Strikes.</summary>
public sealed partial class CFGOAPShootStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedWieldableSystem _wieldable = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Shoot;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } target)
            return;

        EnsureComp<CFGOAPRangedComponent>(ent).Target = target;
        Steering.Hold(ent);
    }

    /// <summary>Wields here and not in <see cref="Begin"/>, because a bow's use delay can refuse the first try.</summary>
    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is { } target && TryComp(ent, out CFGOAPRangedComponent? ranged))
            ranged.Target = target;

        if (_gun.TryGetGun(ent, out var gun)
            && TryComp(gun, out WieldableComponent? wieldable)
            && !wieldable.Wielded)
            _wieldable.TryWield((gun, wieldable), ent);

        return false;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        RemComp<CFGOAPRangedComponent>(ent);
    }
}
