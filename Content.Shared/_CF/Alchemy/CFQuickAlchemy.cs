using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// On the Quick Alchemy action: its charges are the Alchemist's versatile vials. Stock <c>LimitedCharges</c> counts
/// them and <c>AutoRecharge</c> brings them back, and a long rest refills them all.
/// </summary>
[RegisterComponent]
public sealed partial class CFVersatileVialsComponent : Component
{
}

/// <summary>
/// Quick Alchemy and Quick Vial: an item appears in the user's hand and goes inert after a few seconds. Which item is
/// the mode picked on the action's <c>CFModeChoice</c>: mode 1 is the first of <see cref="Items"/>.
/// </summary>
public sealed partial class CFQuickAlchemyEffect : CFEffect
{
    [DataField(required: true)]
    public List<EntProtoId> Items = new();

    /// <summary>How long the item stays potent. The rules say until the user's next turn.</summary>
    [DataField]
    public float LifetimeSeconds = 6f;

    /// <summary>
    /// The items are versatile vials: 1d6 and 1 splash, or 2d6 and 2 splash from level 4. A bomb's dice and splash and a
    /// healing vial's dice are set from the user's level.
    /// </summary>
    [DataField]
    public bool Vial;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (Items.Count == 0)
            return;

        var mode = entities.GetComponentOrNull<CFModeChoiceComponent>(args.Source)?.Chosen ?? 0;
        var index = Math.Clamp(mode - 1, 0, Items.Count - 1);
        entities.System<CFQuickAlchemySystem>().Make(args.User, Items[index], LifetimeSeconds, Vial);
    }
}

/// <summary>Improvise Admixture: a Crafting check gives back 1 to 3 versatile vials, none on a critical failure.</summary>
public sealed partial class CFRegainVialsEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFQuickAlchemySystem>().ImproviseAdmixture(args.User);
    }
}

/// <summary>Quick Bomber: a versatile vial is made and thrown at the target in one action.</summary>
public sealed partial class CFQuickBombEffect : CFEffect
{
    [DataField(required: true)]
    public EntProtoId Vial;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFQuickAlchemySystem>().ThrowVial(args.User, target, Vial);
    }
}

/// <summary>What Quick Alchemy makes, and how many versatile vials an Alchemist has.</summary>
public sealed partial class CFQuickAlchemySystem : EntitySystem
{
    /// <summary>Vials beside the Intelligence modifier.</summary>
    public const int BaseVials = 2;

    /// <summary>A versatile vial gets its second die and second point of splash at this level.</summary>
    public const int SecondDieLevel = 4;

    [Dependency] private Robust.Shared.Network.INetManager _net = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Content.Shared.Throwing.ThrowingSystem _throwing = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private Content.Shared.Popups.SharedPopupSystem _popup = default!;

    /// <summary>How many versatile vials the creature can hold: 2 plus its Intelligence modifier.</summary>
    public int MaxVials(EntityUid user)
    {
        var intelligence = CompOrNull<CFAbilityScoreComponent>(user)?.GetModifier(CFAbility.Intelligence) ?? 0;
        return Math.Max(1, BaseVials + intelligence);
    }

    /// <summary>The Quick Alchemy action whose charges are the creature's vials, if it has one.</summary>
    public EntityUid? VialAction(EntityUid user)
    {
        foreach (var action in _actions.GetActions(user))
        {
            if (HasComp<CFVersatileVialsComponent>(action))
                return action;
        }

        return null;
    }

    /// <summary>Sets the vial pool to its size and fills it. Called when the action is granted and at a long rest.</summary>
    public void Refill(EntityUid user)
    {
        if (VialAction(user) is not { } action)
            return;

        _charges.SetMaxCharges(action, MaxVials(user));
        _charges.ResetCharges(action);
    }

    public int Vials(EntityUid user)
    {
        return VialAction(user) is { } action ? _charges.GetCurrentCharges(action) : 0;
    }

    /// <summary>Makes an item in the user's hand, or at its feet with both hands full, that goes inert after a while.</summary>
    public EntityUid? Make(EntityUid user, EntProtoId item, float seconds, bool vial)
    {
        if (!_net.IsServer)
            return null;

        var made = SpawnAttachedTo(item, Transform(user).Coordinates);
        var options = CompOrNull<CFCombatOptionsComponent>(user);
        if (vial)
            Scale(user, made);

        // Enduring Alchemy: an elixir or tool keeps a turn longer. A bomb does not.
        if (options is { EnduringAlchemy: true } && !HasComp<CFBombComponent>(made))
            seconds *= 2f;

        EnsureComp<TimedDespawnComponent>(made).Lifetime = seconds;
        _hands.TryPickupAnyHand(user, made, animate: false);
        return made;
    }

    /// <summary>A versatile vial's numbers for its maker's level.</summary>
    private void Scale(EntityUid user, EntityUid made)
    {
        var dice = (CompOrNull<CFCombatantComponent>(user)?.Level ?? 1) >= SecondDieLevel ? 2 : 1;
        if (TryComp(made, out CFWeaponComponent? weapon))
        {
            weapon.DamageDice = dice;
            Dirty(made, weapon);
        }

        if (TryComp(made, out CFBombComponent? bomb))
            bomb.Splash = dice;

        if (TryComp(made, out CFHealConsumableComponent? heal))
            heal.HealDice = dice;
    }

    /// <summary>Makes a vial and throws it at the target. The throw is the bomb's Strike.</summary>
    public void ThrowVial(EntityUid user, EntityUid target, EntProtoId vial)
    {
        if (!_net.IsServer)
            return;

        var made = SpawnAttachedTo(vial, Transform(user).Coordinates);
        Scale(user, made);
        EnsureComp<TimedDespawnComponent>(made).Lifetime = 6f;
        var direction = _transform.GetMapCoordinates(target).Position - _transform.GetMapCoordinates(user).Position;
        _throwing.TryThrow(made, direction, user: user);
    }

    /// <summary>Improvise Admixture: Crafting against the DC for the user's level. 3 vials, 2, 1 or none.</summary>
    public void ImproviseAdmixture(EntityUid user)
    {
        if (!_net.IsServer || VialAction(user) is not { } action)
            return;

        var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
        var degree = _conditions.RollSkillAgainst(user, "Crafting", CFAbility.Intelligence, Content.Shared.CF.Crafting.CFCraftingRules.LevelDc(level), out _);
        var gained = degree switch
        {
            CFDegree.CriticalSuccess => 3,
            CFDegree.Success => 2,
            CFDegree.Failure => 1,
            _ => 0,
        };
        var room = MaxVials(user) - _charges.GetCurrentCharges(action);
        gained = Math.Clamp(gained, 0, Math.Max(0, room));
        if (gained > 0)
            _charges.AddCharges(action, gained);

        _popup.PopupEntity(Loc.GetString("cf-alchemy-vials-regained", ("count", gained)), user, user);
    }
}
