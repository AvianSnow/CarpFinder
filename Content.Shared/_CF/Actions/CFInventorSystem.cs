using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>An Inventor's innovation: a worn suit, a held weapon or a construct companion.</summary>
[RegisterComponent]
public sealed partial class CFInnovationComponent : Component
{
}

/// <summary>What an Inventor's armor modifications resist, while the armor innovation is worn.</summary>
[RegisterComponent]
public sealed partial class CFInnovationResistanceComponent : Component
{
    [DataField]
    public List<CFInnovationResistance> Entries = new();
}

[DataDefinition]
public sealed partial class CFInnovationResistance
{
    [DataField(required: true)]
    public List<ProtoId<CFDamageTypePrototype>> Types = new();

    /// <summary>Added to half the creature's level.</summary>
    [DataField]
    public int Flat;
}

/// <summary>An Inventor's Overdrive, and whether their innovation has stopped working.</summary>
[RegisterComponent]
public sealed partial class CFOverdriveComponent : Component
{
    /// <summary>Added to Strike damage until <see cref="Until"/>.</summary>
    [DataField]
    public int Bonus;

    [DataField]
    public TimeSpan Until;

    /// <summary>Overdrive cannot be used before this time.</summary>
    [DataField]
    public TimeSpan Next;

    /// <summary>Unstable actions cannot be used before this time.</summary>
    [DataField]
    public TimeSpan UnstableDownUntil;
}

/// <summary>The Overdrive action.</summary>
[RegisterComponent]
public sealed partial class CFOverdriveActionComponent : Component
{
}

/// <summary>The Explode action. <c>CFAreaEffect</c>, <c>CFCheck</c> and <c>CFDegreeEffects</c> say what it does.</summary>
[RegisterComponent]
public sealed partial class CFExplodeActionComponent : Component
{
}

/// <summary>
/// Inventors. Overdrive adds to Strikes for a minute, on a Crafting check. Unstable actions need the innovation in use, and each one
/// risks it breaking down until it has been retuned, which here just takes as long as Refocus does.
/// </summary>
public sealed partial class CFInventorSystem : EntitySystem
{
    private static readonly TimeSpan OverdriveLasts = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Round = TimeSpan.FromSeconds(6);
    private const int UnstableDc = 15;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFFocusSystem _focus = default!;

    public bool IsWearing(EntityUid user)
    {
        return _armor.TryGetWornArmor(user, out var armor) && HasComp<CFInnovationComponent>(armor);
    }

    /// <summary>The Inventor's construct, if it is alive and theirs.</summary>
    public EntityUid? Construct(EntityUid user)
    {
        var query = EntityQueryEnumerator<CFInnovationComponent, CFCompanionComponent>();
        while (query.MoveNext(out var uid, out _, out var companion))
        {
            if (companion.Summoner == user)
                return uid;
        }

        return null;
    }

    /// <summary>Wearing the suit, wielding the weapon, or having the construct.</summary>
    public bool IsUsing(EntityUid user)
    {
        if (IsWearing(user))
            return true;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFInnovationComponent>(held) && !HasComp<CFArmorComponent>(held))
                return true;
        }

        return Construct(user) != null;
    }

    public bool IsOverdriven(EntityUid user)
    {
        return TryComp(user, out CFOverdriveComponent? overdrive) && _timing.CurTime < overdrive.Until;
    }

    /// <summary>What Overdrive adds to this creature's Strikes.</summary>
    public int OverdriveDamage(EntityUid user)
    {
        return TryComp(user, out CFOverdriveComponent? overdrive) && _timing.CurTime < overdrive.Until ? overdrive.Bonus : 0;
    }

    /// <summary>What the armor innovation resists of a damage type, more under Overdrive.</summary>
    public int Resistance(EntityUid target, string damageType)
    {
        if (!TryComp(target, out CFInnovationResistanceComponent? resist) || !IsWearing(target))
            return 0;

        var best = 0;
        var level = CompOrNull<CFCombatantComponent>(target)?.Level ?? 1;
        foreach (var entry in resist.Entries)
        {
            if (entry.Types.Exists(type => CFDamageTypes.Same(_prototypes, type, damageType)))
                best = Math.Max(best, entry.Flat + level / 2);
        }

        return best > 0 && IsOverdriven(target) ? best + 2 : best;
    }

    public void AddResistance(EntityUid mob, CFInnovationResistance entry)
    {
        EnsureComp<CFInnovationResistanceComponent>(mob).Entries.Add(entry);
    }

    public bool CanUseUnstable(EntityUid user)
    {
        return IsUsing(user) && (!TryComp(user, out CFOverdriveComponent? overdrive) || _timing.CurTime >= overdrive.UnstableDownUntil);
    }

    /// <summary>The flat check after an unstable action. A failure stops the innovation, and a critical failure also burns.</summary>
    public void RollUnstable(EntityUid user)
    {
        if (!_net.IsServer)
            return;

        var degree = CFDegrees.DegreeOfSuccess(_random.Next(1, 21), 0, UnstableDc);
        if (degree.IsSuccess())
            return;

        EnsureComp<CFOverdriveComponent>(user).UnstableDownUntil = _timing.CurTime + _focus.RefocusTime();
        _popup.PopupEntity(Loc.GetString("cf-inventor-unstable-down"), user, user);
        if (degree == CFDegree.CriticalFailure)
            _effects.DealDamage(user, user, Math.Max(1, (CompOrNull<CFCombatantComponent>(user)?.Level ?? 1) / 2), "Fire", critical: false);
    }

    [SubscribeLocalEvent]
    private void OnOverdriveAttempt(Entity<CFOverdriveActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (!TryComp(args.User, out CFOverdriveComponent? overdrive) || _timing.CurTime >= overdrive.Next)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-inventor-overdrive-wait");
    }

    [SubscribeLocalEvent]
    private void OnOverdrive(Entity<CFOverdriveActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            Overdrive(args.Performer);
    }

    private void Overdrive(EntityUid user)
    {
        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return;

        var overdrive = EnsureComp<CFOverdriveComponent>(user);
        var active = _timing.CurTime < overdrive.Until;
        var modifier = CFDefense.SkillModifier(_prototypes, combat, abilities, "Crafting", CFAbility.Intelligence, _conditions.SkillBonus(user, "Crafting"));
        var degree = CFDefense.Roll(_random, modifier, CFCraftingRules.LevelDc(combat.Level));
        var intelligence = Math.Max(0, abilities.GetModifier(CFAbility.Intelligence));
        var expert = CompOrNull<CFCombatOptionsComponent>(user)?.ExpertOverdrive == true ? 1 : 0;

        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                Begin(overdrive, intelligence + expert, active);
                _popup.PopupEntity(Loc.GetString("cf-inventor-overdrive-critical"), user, user);
                break;
            case CFDegree.Success when !active:
                Begin(overdrive, intelligence / 2 + expert, active: false);
                _popup.PopupEntity(Loc.GetString("cf-inventor-overdrive"), user, user);
                break;
            case CFDegree.Failure when !active:
                Begin(overdrive, 1, active: false);
                _popup.PopupEntity(Loc.GetString("cf-inventor-overdrive-smoke"), user, user);
                break;
            case CFDegree.CriticalFailure:
                overdrive.Until = TimeSpan.Zero;
                overdrive.Next = _timing.CurTime + Round * _random.Next(1, 5);
                _effects.DealDamage(user, user, (combat.Level + 1) / 2, "Fire", critical: false);
                _popup.PopupEntity(Loc.GetString("cf-inventor-overdrive-explodes"), user, user);
                break;
        }
    }

    private void Begin(CFOverdriveComponent overdrive, int bonus, bool active)
    {
        overdrive.Bonus = bonus;
        if (active)
            return;

        overdrive.Until = _timing.CurTime + OverdriveLasts;
        overdrive.Next = overdrive.Until + OverdriveLasts;
    }

    [SubscribeLocalEvent]
    private void OnExplodeAttempt(Entity<CFExplodeActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (CanUseUnstable(args.User))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-inventor-unstable-cannot");
    }

    [SubscribeLocalEvent]
    private void OnExplode(Entity<CFExplodeActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        // A construct explodes where it stands. Anything else is worn or held.
        var origin = !IsWearing(args.Performer) && Construct(args.Performer) is { } construct && !HoldsInnovation(args.Performer)
            ? _transform.GetMapCoordinates(construct)
            : _transform.GetMapCoordinates(args.Performer);

        _effects.ResolveArea(ent, args.Performer, origin);
        RollUnstable(args.Performer);
    }

    private bool HoldsInnovation(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFInnovationComponent>(held))
                return true;
        }

        return false;
    }
}
