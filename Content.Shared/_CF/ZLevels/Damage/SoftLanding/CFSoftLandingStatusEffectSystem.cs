// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Analyzers;

namespace Content.Shared.CF.ZLevels.Damage.SoftLanding;

public sealed partial class CFSoftLandingStatusEffectSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standingState = default!;

    [SubscribeLocalEvent]
    private void OnFallingDamageCalculate(Entity<CFSoftLandingStatusEffectComponent> ent, ref StatusEffectRelayedEvent<CFZFallingDamageCalculateEvent> args)
    {
        var target = args.AppliedTo;
        var fallArgs = args.Args;

        if (_standingState.IsDown(target))
            return;

        if (fallArgs.Speed <= ent.Comp.MaxSpeedLimit)
        {
            fallArgs.DamageMultiplier *= ent.Comp.DamageMultiplier;
            fallArgs.StunMultiplier *= ent.Comp.StunMultiplier;

            _popup.PopupPredicted(Loc.GetString("cf-soft-landing"), target, target);
        }
        else
        {
            fallArgs.DamageMultiplier *= ent.Comp.DamageHardFallMultiplier;
            fallArgs.StunMultiplier *= ent.Comp.StunHardFallMultiplier;

            _popup.PopupPredicted(Loc.GetString("cf-soft-landing-too-high"), target, target, PopupType.SmallCaution);
        }
    }
}
