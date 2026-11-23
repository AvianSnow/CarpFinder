using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Locks;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
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
    /// <summary>A construct has had its Inventor's modifications applied, so a second pass does not double them.</summary>
    [DataField]
    public bool ModificationsApplied;
}

/// <summary>
/// What an Inventor has chosen for their innovation, kept on the Inventor so a weapon or construct can be made again from it:
/// after Collapse Construct, Dual-Form Weapon's switch, or a long rest that rebuilds a destroyed construct.
/// </summary>
[RegisterComponent]
public sealed partial class CFInventorComponent : Component
{
    /// <summary>What the innovation explodes with (Variable Core). Null is fire.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? Core;

    /// <summary>The weapon the weapon innovation is built on.</summary>
    [DataField]
    public EntProtoId? WeaponBase;

    /// <summary>Dual-Form Weapon's other base. The modification is the same on both.</summary>
    [DataField]
    public EntProtoId? SecondBase;

    /// <summary>Traits the modification adds to the base weapon.</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> WeaponTraits = new();

    /// <summary>Damage types a modular head switches between. Empty means none.</summary>
    [DataField]
    public List<ProtoId<CFDamageTypePrototype>> WeaponModes = new();

    /// <summary>The weapon's damage die is one step larger (Complex Simplicity).</summary>
    [DataField]
    public bool WeaponDieStep;

    /// <summary>Two-hand with a die one step larger than the base (Dynamic Weighting).</summary>
    [DataField]
    public bool WeaponTwoHand;

    /// <summary>The construct's Speed in feet, or 0 for the body's own (Accelerated Mobility).</summary>
    [DataField]
    public int ConstructSpeedFeet;

    /// <summary>The construct sees in the dark (Sensory Array).</summary>
    [DataField]
    public bool ConstructDarkvision;

    /// <summary>Skills the construct is trained in (Wonder Gears).</summary>
    [DataField]
    public List<string> ConstructSkills = new();

    /// <summary>Advanced Construct Companion: better attributes, two damage dice, expert saves.</summary>
    [DataField]
    public bool AdvancedConstruct;

    /// <summary>Built-In Tools: thieves' tools are part of the innovation.</summary>
    [DataField]
    public bool BuiltInTools;

    /// <summary>Reverse Engineer: Crafting in place of Thievery to Pick a Lock or Disable a Device.</summary>
    [DataField]
    public bool CraftingForThievery;
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

/// <summary>Tamper: the target has to hold a weapon or wear armor, and the user needs a free hand. The effect does the rest.</summary>
[RegisterComponent]
public sealed partial class CFTamperActionComponent : Component
{
}

/// <summary>Collapse Armor: the worn armor innovation comes off into a hand at once, or a held one goes on.</summary>
[RegisterComponent]
public sealed partial class CFCollapseArmorActionComponent : Component
{
}

/// <summary>Collapse Construct: the construct folds into a case that is carried, and unfolds from it. The server runs it.</summary>
[RegisterComponent]
public sealed partial class CFCollapseConstructActionComponent : Component
{
}

/// <summary>Haphazard Repair: Repair the construct innovation in one action, as an unstable action.</summary>
[RegisterComponent]
public sealed partial class CFHaphazardRepairActionComponent : Component
{
}

/// <summary>Dual-Form Weapon's switch: the held weapon innovation becomes its other configuration.</summary>
[RegisterComponent]
public sealed partial class CFSwitchConfigurationActionComponent : Component
{
}

/// <summary>A construct folded up by Collapse Construct, with what it had left.</summary>
[RegisterComponent]
public sealed partial class CFCollapsedConstructComponent : Component
{
    [DataField]
    public EntityUid Inventor;

    [DataField]
    public int CurrentHp;
}

/// <summary>
/// Inventors. Overdrive adds to Strikes for a minute, on a Crafting check. Unstable actions need the innovation in use, and each one
/// risks it breaking down until it has been retuned, which here just takes as long as Refocus does. The weapon innovation is
/// built from a base weapon and its modification, and the construct takes its modifications when it is spawned.
/// </summary>
public sealed partial class CFInventorSystem : EntitySystem
{
    private static readonly TimeSpan OverdriveLasts = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Round = TimeSpan.FromSeconds(6);
    private const int UnstableDc = 15;
    private const string ArmorSlot = "outerClothing";
    private const string Crafting = "Crafting";
    private const float BaselineSpeedFeet = 25f;

    /// <summary>How far an innovation can be from its Inventor and still count as adjacent, in tiles.</summary>
    public const float AdjacentTiles = 1.6f;

    public static readonly ProtoId<CFDamageTypePrototype> Fire = "Fire";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFFocusSystem _focus = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;

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
            if (companion.Summoner == user && !_mobState.IsDead(uid))
                return uid;
        }

        return null;
    }

    /// <summary>The Inventor's construct, when it stands close enough to be worked on.</summary>
    public EntityUid? ConstructInReach(EntityUid user)
    {
        return Construct(user) is { } construct && _transform.GetMapCoordinates(user).InRange(_transform.GetMapCoordinates(construct), AdjacentTiles)
            ? construct
            : null;
    }

    /// <summary>The weapon innovation in a hand, if any.</summary>
    public EntityUid? HeldInnovation(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFInnovationComponent>(held) && !HasComp<CFArmorComponent>(held))
                return held;
        }

        return null;
    }

    /// <summary>Wearing the suit, wielding the weapon, or having the construct.</summary>
    public bool IsUsing(EntityUid user)
    {
        return IsWearing(user) || HeldInnovation(user) != null || Construct(user) != null;
    }

    /// <summary>The innovation in use: the worn suit, the held weapon, or the construct, in that order.</summary>
    public EntityUid? InnovationInUse(EntityUid user)
    {
        if (_armor.TryGetWornArmor(user, out var armor) && HasComp<CFInnovationComponent>(armor))
            return armor;

        return HeldInnovation(user) ?? Construct(user);
    }

    /// <summary>The Inventor a construct innovation belongs to, or the creature itself.</summary>
    private EntityUid Owner(EntityUid user)
    {
        return HasComp<CFInnovationComponent>(user) && TryComp(user, out CFCompanionComponent? companion) && companion.Summoner.IsValid()
            ? companion.Summoner
            : user;
    }

    public bool IsOverdriven(EntityUid user)
    {
        return TryComp(Owner(user), out CFOverdriveComponent? overdrive) && _timing.CurTime < overdrive.Until;
    }

    /// <summary>What Overdrive adds to this creature's Strikes. A construct innovation shares its Inventor's.</summary>
    public int OverdriveDamage(EntityUid user)
    {
        return TryComp(Owner(user), out CFOverdriveComponent? overdrive) && _timing.CurTime < overdrive.Until ? overdrive.Bonus : 0;
    }

    /// <summary>What the innovation explodes with: fire, unless Variable Core changed it.</summary>
    public ProtoId<CFDamageTypePrototype> CoreType(EntityUid user)
    {
        return CompOrNull<CFInventorComponent>(Owner(user))?.Core ?? Fire;
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
            _effects.DealDamage(user, user, Math.Max(1, (CompOrNull<CFCombatantComponent>(user)?.Level ?? 1) / 2), CoreType(user), critical: false);
    }

    public bool HasBuiltInTools(EntityUid user)
    {
        return CompOrNull<CFInventorComponent>(user)?.BuiltInTools == true && IsUsing(user);
    }

    /// <summary>
    /// Built-In Tools: thieves' tools that are part of the innovation in use. The toolkit lives on the innovation, so a
    /// critical failure breaks it as it would any other, until the innovation is made again.
    /// </summary>
    public Entity<CFThievesToolkitComponent>? BuiltInToolkit(EntityUid user)
    {
        if (!HasBuiltInTools(user) || InnovationInUse(user) is not { } innovation)
            return null;

        var toolkit = EnsureComp<CFThievesToolkitComponent>(innovation);
        return toolkit.Broken ? null : (innovation, toolkit);
    }

    /// <summary>
    /// Reverse Engineer: Crafting stands in for Thievery when it is the better of the two. Returns the skill to roll and its
    /// attribute.
    /// </summary>
    public (string Skill, CFAbility Ability) ThieverySkill(EntityUid user)
    {
        if (CompOrNull<CFInventorComponent>(user)?.CraftingForThievery != true
            || !TryComp(user, out CFCombatantComponent? combat)
            || !TryComp(user, out CFAbilityScoreComponent? abilities))
        {
            return ("Thievery", CFAbility.Dexterity);
        }

        var thievery = CFDefense.SkillModifier(_prototypes, combat, abilities, "Thievery", CFAbility.Dexterity, _conditions.SkillBonus(user, "Thievery"));
        var crafting = CFDefense.SkillModifier(_prototypes, combat, abilities, Crafting, CFAbility.Intelligence, _conditions.SkillBonus(user, Crafting));
        return crafting > thievery ? (Crafting, CFAbility.Intelligence) : ("Thievery", CFAbility.Dexterity);
    }

    // Overdrive

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
        var modifier = CFDefense.SkillModifier(_prototypes, combat, abilities, Crafting, CFAbility.Intelligence, _conditions.SkillBonus(user, Crafting));
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
                var burn = (combat.Level + 1) / 2;
                _effects.DealDamage(user, user, burn, CoreType(user), critical: false);
                // A construct innovation runs on the same core, and takes the same blast.
                if (Construct(user) is { } construct)
                    _effects.DealDamage(user, construct, burn, CoreType(user), critical: false);

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

    // Explode

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
        var origin = !IsWearing(args.Performer) && Construct(args.Performer) is { } construct && HeldInnovation(args.Performer) == null
            ? _transform.GetMapCoordinates(construct)
            : _transform.GetMapCoordinates(args.Performer);

        _effects.ResolveArea(ent, args.Performer, origin);
        RollUnstable(args.Performer);
    }

    // Tamper

    [SubscribeLocalEvent]
    private void OnTamperAttempt(Entity<CFTamperActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Target is not { } target || HeldWeapon(target) != null || _armor.TryGetWornArmor(target, out _))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-inventor-tamper-nothing");
    }

    /// <summary>A weapon the creature holds, for Tamper.</summary>
    public EntityUid? HeldWeapon(EntityUid creature)
    {
        foreach (var held in _hands.EnumerateHeld(creature))
        {
            if (HasComp<CFWeaponComponent>(held))
                return held;
        }

        return null;
    }

    // Collapse Armor

    [SubscribeLocalEvent]
    private void OnCollapseArmorAttempt(Entity<CFCollapseArmorActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (IsWearing(args.User) || HeldArmorInnovation(args.User) != null)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-inventor-no-armor");
    }

    [SubscribeLocalEvent]
    private void OnCollapseArmor(Entity<CFCollapseArmorActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var user = args.Performer;
        if (_armor.TryGetWornArmor(user, out var worn) && HasComp<CFInnovationComponent>(worn))
        {
            if (!_inventory.TryGetContainingSlot(worn.Owner, out var slot) || !_inventory.TryUnequip(user, slot.Name, out var removed, force: true))
                return;

            _hands.TryPickupAnyHand(user, removed.Value, animate: false);
            _popup.PopupEntity(Loc.GetString("cf-inventor-armor-collapsed"), user, user);
            return;
        }

        if (HeldArmorInnovation(user) is not { } held)
            return;

        if (_inventory.TryGetSlotEntity(user, ArmorSlot, out _))
        {
            _popup.PopupEntity(Loc.GetString("cf-inventor-slot-taken"), user, user);
            return;
        }

        _hands.TryDrop(user, held, checkActionBlocker: false);
        if (_inventory.TryEquip(user, held, ArmorSlot, force: true))
            _popup.PopupEntity(Loc.GetString("cf-inventor-armor-unfolded"), user, user);
        else
            _hands.TryPickupAnyHand(user, held, animate: false);
    }

    private EntityUid? HeldArmorInnovation(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFInnovationComponent>(held) && HasComp<CFArmorComponent>(held))
                return held;
        }

        return null;
    }

    // Haphazard Repair

    [SubscribeLocalEvent]
    private void OnRepairAttempt(Entity<CFHaphazardRepairActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (!CanUseUnstable(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-inventor-unstable-cannot");
            return;
        }

        if (ConstructInReach(args.User) != null)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-inventor-nothing-to-repair");
    }

    /// <summary>
    /// Repair in one action: Crafting against the level's DC. A success restores 10 Hit Points and 5 more per rank of Crafting,
    /// a critical success 10 more per rank, and a critical failure deals 2d6 to the construct. Then the unstable check.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRepair(Entity<CFHaphazardRepairActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var user = args.Performer;
        if (ConstructInReach(user) is not { } construct
            || !TryComp(user, out CFCombatantComponent? combat)
            || !TryComp(construct, out CFCombatantComponent? body))
        {
            return;
        }

        var steps = (int) CFDefense.SkillRank(_prototypes, combat, Crafting) / 2;
        var degree = _conditions.RollSkillAgainst(user, Crafting, CFAbility.Intelligence, CFCraftingRules.LevelDc(combat.Level), out _);
        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                Mend(construct, body, 10 + 10 * steps);
                _popup.PopupEntity(Loc.GetString("cf-inventor-repaired"), user, user);
                break;
            case CFDegree.Success:
                Mend(construct, body, 10 + 5 * steps);
                _popup.PopupEntity(Loc.GetString("cf-inventor-repaired"), user, user);
                break;
            case CFDegree.Failure:
                _popup.PopupEntity(Loc.GetString("cf-inventor-repair-failed"), user, user);
                break;
            case CFDegree.CriticalFailure:
                _effects.DealDamage(user, construct, _effects.Roll(2, 6, 0), CFDamageTypes.Bludgeoning, critical: false);
                _popup.PopupEntity(Loc.GetString("cf-inventor-repair-botched"), user, user);
                break;
        }

        RollUnstable(user);
    }

    /// <summary>Constructs are repaired, not healed: a Heal does nothing to one, so the Hit Points are written here.</summary>
    public void Mend(EntityUid construct, CFCombatantComponent body, int amount)
    {
        if (amount <= 0 || body.CurrentHp <= 0)
            return;

        SetHitPoints(construct, body, body.CurrentHp + amount);
    }

    /// <summary>Sets a construct's Hit Points, between 1 and its most, as unfolding one gives back what it had.</summary>
    public void SetHitPoints(EntityUid construct, CFCombatantComponent body, int hitPoints)
    {
        body.CurrentHp = Math.Clamp(hitPoints, 1, body.MaxHp);
        Dirty(construct, body);
        _dying.SyncHealthBar(construct, body);
    }

    // Dual-Form Weapon

    [SubscribeLocalEvent]
    private void OnSwitchAttempt(Entity<CFSwitchConfigurationActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (CompOrNull<CFInventorComponent>(args.User)?.SecondBase == null)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-inventor-no-second-form");
            return;
        }

        if (HeldInnovation(args.User) != null)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-strike-action-needs-innovation");
    }

    [SubscribeLocalEvent]
    private void OnSwitch(Entity<CFSwitchConfigurationActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var user = args.Performer;
        if (HeldInnovation(user) is not { } held
            || !TryComp(user, out CFInventorComponent? inventor)
            || inventor.SecondBase is not { } second
            || inventor.WeaponBase is not { } first)
        {
            return;
        }

        var next = MetaData(held).EntityPrototype?.ID == second.Id ? first : second;
        var made = MakeWeapon(user, inventor, next);
        _hands.TryDrop(user, held, checkActionBlocker: false);
        Del(held);
        _hands.TryPickupAnyHand(user, made, animate: false);
        _popup.PopupEntity(Loc.GetString("cf-inventor-switched", ("weapon", made)), user, user);
    }

    // The weapon innovation

    /// <summary>
    /// Builds the weapon innovation: the base weapon with the modification's traits, damage modes and die step, named after the
    /// base. It is spawned at the Inventor's feet; the caller stows it.
    /// </summary>
    public EntityUid MakeWeapon(EntityUid owner, CFInventorComponent inventor, EntProtoId baseWeapon)
    {
        var item = SpawnAtPosition(baseWeapon, Transform(owner).Coordinates);
        EnsureComp<CFInnovationComponent>(item);

        var traits = EnsureComp<CFTraitComponent>(item);
        foreach (var trait in inventor.WeaponTraits)
        {
            traits.Traits.Add(trait);
        }

        if (TryComp(item, out CFWeaponComponent? weapon))
        {
            if (inventor.WeaponDieStep)
                weapon.DamageDie = StepUp(weapon.DamageDie);

            if (inventor.WeaponTwoHand && weapon.Hands == CFWeaponHands.One)
                traits.Traits.Add(new ProtoId<CFTraitPrototype>("TwoHandD" + StepUp(weapon.DamageDie)));

            Dirty(item, weapon);
        }

        Dirty(item, traits);

        if (inventor.WeaponModes.Count > 0)
        {
            var modes = EnsureComp<CFDamageModeComponent>(item);
            modes.Types = new List<ProtoId<CFDamageTypePrototype>>(inventor.WeaponModes);
            modes.Index = 0;
            Dirty(item, modes);
        }

        if (inventor.BuiltInTools)
            EnsureComp<CFThievesToolkitComponent>(item);

        var meta = MetaData(item);
        _metaData.SetEntityName(item, Loc.GetString("cf-innovation-weapon-name", ("weapon", meta.EntityName)), meta);
        return item;
    }

    /// <summary>The next die size up, to a d12.</summary>
    public static int StepUp(int die)
    {
        return die switch
        {
            < 6 => 6,
            6 => 8,
            8 => 10,
            _ => 12,
        };
    }

    // The construct innovation

    /// <summary>
    /// Gives a construct its Inventor's modifications: Speed, darkvision, skills, and an advanced construct's attributes, dice
    /// and ranks. The server calls it when the construct spawns, and a grant calls it on a construct already out.
    /// </summary>
    public void ApplyConstructModifications(EntityUid inventorUid, EntityUid construct)
    {
        if (!TryComp(inventorUid, out CFInventorComponent? inventor) || !TryComp(construct, out CFInnovationComponent? innovation) || innovation.ModificationsApplied)
            return;

        innovation.ModificationsApplied = true;
        var combat = EnsureComp<CFCombatantComponent>(construct);

        if (inventor.ConstructSpeedFeet > 0)
        {
            combat.SpeedFeet = inventor.ConstructSpeedFeet;
            if (TryComp(construct, out MovementSpeedModifierComponent? move))
            {
                var scale = inventor.ConstructSpeedFeet / BaselineSpeedFeet;
                _speed.ChangeBaseSpeed(construct, move.BaseWalkSpeed * scale, move.BaseSprintSpeed * scale, move.BaseAcceleration, move);
            }
        }

        if (inventor.ConstructDarkvision)
            _features.GrantSense(construct, CFSense.Darkvision);

        var skillRank = inventor.AdvancedConstruct ? CFProficiencyRank.Expert : CFProficiencyRank.Trained;
        foreach (var skill in inventor.ConstructSkills)
        {
            combat.ProficiencyRanks[skill] = skillRank;
        }

        if (inventor.AdvancedConstruct)
        {
            foreach (var skill in new[] { "Intimidation", "Stealth", "Survival" })
            {
                if (combat.ProficiencyRanks.GetValueOrDefault(skill) < CFProficiencyRank.Trained)
                    combat.ProficiencyRanks[skill] = CFProficiencyRank.Trained;
            }

            foreach (var save in new[] { "Perception", "Fortitude", "Reflex", "Will" })
            {
                if (combat.ProficiencyRanks.GetValueOrDefault(save) < CFProficiencyRank.Expert)
                    combat.ProficiencyRanks[save] = CFProficiencyRank.Expert;
            }

            if (TryComp(construct, out CFAbilityScoreComponent? abilities))
            {
                foreach (var ability in new[] { CFAbility.Strength, CFAbility.Dexterity, CFAbility.Constitution, CFAbility.Wisdom })
                {
                    abilities.AbilityModifiers[ability] = abilities.GetModifier(ability) + 1;
                }

                Dirty(construct, abilities);
            }

            var options = EnsureComp<CFCombatOptionsComponent>(construct);
            options.NaturalDice = Math.Max(2, options.NaturalDice);
            Dirty(construct, options);
        }

        Dirty(construct, combat);
    }
}
