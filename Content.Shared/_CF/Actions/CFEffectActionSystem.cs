using Content.Shared.Actions.Events;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Network;

namespace Content.Shared.CF.Actions;

/// <summary>Runs actions that only apply effects. See <see cref="CFEffectActionComponent"/>.</summary>
public sealed partial class CFEffectActionSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFInventorSystem _inventor = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;
    [Dependency] private Content.Shared.CF.Factions.CFFactionSystem _factions = default!;

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFEffectActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (ent.Comp.NeedsMediumOrHeavyArmor
            && !(_armor.TryGetWornArmor(args.User, out var armor)
                 && armor.Comp.Category is CFArmorCategory.Medium or CFArmorCategory.Heavy))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-act-needs-armor");
            return;
        }

        if (ent.Comp.NeedsBloodMagic && !EntityManager.System<Content.Shared.CF.Spells.CFBloodMagicSystem>().Recent(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-act-needs-blood-magic");
            return;
        }

        var curses = EntityManager.System<Content.Shared.CF.Spells.CFCurseboundSystem>();
        if (ent.Comp.Cursebound && !curses.CanRaise(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-oracle-cursebound-max");
            return;
        }

        if (ent.Comp.NeedsDamagingSpell && !curses.DamagingSpellRecent(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-oracle-needs-spell");
            return;
        }

        if (ent.Comp.NeedsRaging && CompOrNull<Content.Shared.CF.Combat.CFCombatOptionsComponent>(args.User)?.Raging != true)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-strike-action-needs-rage");
            return;
        }

        if (ent.Comp.NeedsDeadEnemy
            && (args.Target is not { } corpse || !_mobState.IsDead(corpse) || !_factions.CanHarm(args.User, corpse)))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-act-needs-dead-enemy");
            return;
        }

        if (ent.Comp.NeedsTwoHanded != CFTwoHandedNeed.No && !HoldsTwoHanded(args.User, ent.Comp.NeedsTwoHanded))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-strike-action-needs-two-handed");
            return;
        }

        if (ent.Comp.NeedsWeaponAndFreeHand && !HoldsOneWeaponOnly(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-strike-action-needs-free-hand");
            return;
        }

        if (ent.Comp.Unstable && !_inventor.CanUseUnstable(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-inventor-unstable-cannot");
            return;
        }

        if (ent.Comp.NeedsFreeHand && _hands.CountFreeHands(args.User) == 0)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-act-needs-free-hand");
            return;
        }

        if (ent.Comp.NeedsGrabbedFoe && EntityManager.System<CFConditionSystem>().Grabbing(args.User) == null)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-act-needs-grabbed");
            return;
        }

        if (ent.Comp.NeedsBothHandsFree && _hands.CountFreeHands(args.User) < 2)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-act-needs-both-hands");
            return;
        }

        if (!ent.Comp.NeedsWeaponInEachHand)
            return;

        var weapons = 0;
        foreach (var held in _hands.EnumerateHeld(args.User))
        {
            if (HasComp<CFMeleeWeaponComponent>(held))
                weapons++;
        }

        if (weapons >= 2)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-act-needs-two-weapons");
    }

    private bool HoldsTwoHanded(EntityUid user, CFTwoHandedNeed need)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (CompOrNull<CFWeaponComponent>(held)?.Hands != CFWeaponHands.Two)
                continue;

            if (need == CFTwoHandedNeed.Ranged ? HasComp<CFRangedWeaponComponent>(held) : HasComp<CFMeleeWeaponComponent>(held))
                return true;
        }

        return false;
    }

    private bool HoldsOneWeaponOnly(EntityUid user)
    {
        if (_hands.CountFreeHands(user) == 0)
            return false;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFMeleeWeaponComponent>(held) && CompOrNull<CFWeaponComponent>(held)?.Hands != CFWeaponHands.Two)
                return true;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFEffectActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        // An action with an area around its user reaches everyone in it (Oracular Warning).
        if (HasComp<CFAreaEffectComponent>(ent))
            _effects.ResolveArea(ent, args.Performer, _transform.GetMapCoordinates(args.Performer));
        else if (ent.Comp.NeedsGrabbedFoe)
        {
            if (EntityManager.System<CFConditionSystem>().Grabbing(args.Performer) is { } grabbed)
                _effects.ResolveTarget(ent, args.Performer, grabbed);
        }
        else
            _effects.ResolveTarget(ent, args.Performer, args.Performer);

        if (ent.Comp.Cursebound)
            EntityManager.System<Content.Shared.CF.Spells.CFCurseboundSystem>().Raise(args.Performer);

        if (ent.Comp.Unstable)
            _inventor.RollUnstable(args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFEffectActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        _effects.ResolveTarget(ent, args.Performer, args.Target);
        if (ent.Comp.Cursebound)
            EntityManager.System<Content.Shared.CF.Spells.CFCurseboundSystem>().Raise(args.Performer);

        if (ent.Comp.Unstable)
            _inventor.RollUnstable(args.Performer);
    }
}
