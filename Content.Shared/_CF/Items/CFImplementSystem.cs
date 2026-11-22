using Content.Shared.Actions.Events;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Wieldable.Components;

namespace Content.Shared.CF.Items;

public enum CFImplementKind : byte
{
    Amulet,
    Bell,
    Censer,
    Chalice,
    Lantern,
    Mirror,
    Regalia,
    Shield,
    Tome,
    Wand,
    Weapon,
}

/// <summary>An item a Thaumaturge channels power through. See <see cref="CFImplementSystem"/>.</summary>
[RegisterComponent]
public sealed partial class CFImplementComponent : Component
{
    [DataField(required: true)]
    public CFImplementKind Kind;
}

/// <summary>An action that cannot be used without this kind of implement in hand.</summary>
[RegisterComponent]
public sealed partial class CFNeedsImplementComponent : Component
{
    [DataField(required: true)]
    public CFImplementKind Kind;
}

/// <summary>Answers what a Thaumaturge is holding. A weapon implement is not one item: any one-handed weapon held counts.</summary>
public sealed partial class CFImplementSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    public bool IsHolding(EntityUid user, CFImplementKind kind)
    {
        var weaponCounts = kind == CFImplementKind.Weapon && CompOrNull<CFCombatOptionsComponent>(user)?.WeaponImplement == true;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp(held, out CFImplementComponent? implement) && implement.Kind == kind)
                return true;

            if (weaponCounts && IsOneHandedWeapon(held))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Implement's Empowerment needs an implement in hand, and nothing else in either hand but one one-handed weapon.
    /// </summary>
    public bool Empowers(EntityUid user)
    {
        var weaponCounts = CompOrNull<CFCombatOptionsComponent>(user)?.WeaponImplement == true;
        var implement = false;
        var weapon = false;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFImplementComponent>(held))
            {
                implement = true;
                continue;
            }

            if (weapon || !IsOneHandedWeapon(held))
                return false;

            weapon = true;
            implement |= weaponCounts;
        }

        return implement;
    }

    private bool IsOneHandedWeapon(EntityUid item)
    {
        return TryComp(item, out CFWeaponComponent? weapon)
               && weapon.Hands != CFWeaponHands.Two
               && CompOrNull<WieldableComponent>(item)?.Wielded != true;
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFNeedsImplementComponent> ent, ref ActionAttemptEvent args)
    {
        if (IsHolding(args.User, ent.Comp.Kind))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-implement-needed");
    }
}
