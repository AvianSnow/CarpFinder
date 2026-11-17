using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Wall;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>Something else that has to be true for a critical specialization to apply.</summary>
public enum CFCriticalSpecializationWhen : byte
{
    Always,
    Raging,

    /// <summary>The target is off-guard to the attacker.</summary>
    TargetOffGuard,

    /// <summary>The target is the attacker's hunted prey.</summary>
    TargetIsPrey,
}

/// <summary>
/// One way a character has critical specialization: which weapons, and when. Written in YAML under
/// <c>CFGrantCriticalSpecialization</c>. Every field that is set has to hold.
/// </summary>
[DataDefinition]
public sealed partial class CFCriticalSpecialization
{
    /// <summary>Only weapons of these groups. Empty means any.</summary>
    [DataField]
    public List<CFWeaponGroup> Groups = new();

    /// <summary>Only these weapons, or weapons with one of <see cref="WeaponTraits"/>. Both empty means any.</summary>
    [DataField]
    public List<EntProtoId> Weapons = new();

    [DataField]
    public List<ProtoId<CFTraitPrototype>> WeaponTraits = new();

    [DataField]
    public bool AgileOrFinesse;

    [DataField]
    public bool MeleeOnly;

    [DataField]
    public bool UnarmedOnly;

    /// <summary>Only with an Inventor's weapon innovation.</summary>
    [DataField]
    public bool InnovationOnly;

    /// <summary>Only a weapon a Ruffian can Sneak Attack with: simple up to d8, martial or advanced up to d6.</summary>
    [DataField]
    public bool SneakAttackWeapon;

    /// <summary>The attacker's proficiency with the weapon has to be at least this.</summary>
    [DataField]
    public CFProficiencyRank MinRank = CFProficiencyRank.Untrained;

    /// <summary>The attacker has to be at least this level, as an ancestry's weapon familiarity asks for level 5.</summary>
    [DataField]
    public int MinLevel;

    [DataField]
    public CFCriticalSpecializationWhen When = CFCriticalSpecializationWhen.Always;

    /// <summary>The effect of this group is used, not the weapon's own (Student of the Staff uses the club's).</summary>
    [DataField]
    public CFWeaponGroup? As;
}

/// <summary>The ways a creature has critical specialization. Empty or missing means it has none.</summary>
[RegisterComponent]
public sealed partial class CFCriticalSpecializationsComponent : Component
{
    [DataField]
    public List<CFCriticalSpecialization> Rules = new();
}

/// <summary>
/// Critical specialization: what a weapon's group does on a critical hit, for a character who has it with that weapon.
/// <see cref="CFStrikeSystem"/> calls <see cref="TryApply"/> after a critical hit's damage. Times the rules give as
/// "until your next turn" are 6 seconds. Stunned 1 takes one action from the target, and a polearm pushes away, where the
/// rules let the attacker choose the direction. The bomb's larger splash is not built, because there are no bombs.
/// </summary>
public sealed partial class CFCriticalSpecializationSystem : EntitySystem
{
    private const float Seconds = 6f;

    /// <summary>How close a wall has to be for an arrow to pin its target to it, in tiles.</summary>
    private const float PinRange = 1.1f;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFFlankingSystem _flanking = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public void Add(EntityUid mob, CFCriticalSpecialization rule)
    {
        EnsureComp<CFCriticalSpecializationsComponent>(mob).Rules.Add(rule);
    }

    /// <summary>The group a Strike's weapon belongs to. Fists and natural attacks are brawling.</summary>
    public CFWeaponGroup GroupOf(CFStrikeWeaponStats weapon)
    {
        return weapon.Item is { } item && TryComp(item, out CFWeaponComponent? data) ? data.Group : CFWeaponGroup.Brawling;
    }

    /// <summary>
    /// The group whose effect this critical hit gets, or null when the attacker has no critical specialization that covers it.
    /// </summary>
    public CFWeaponGroup? Applies(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, bool melee)
    {
        if (!TryComp(user, out CFCriticalSpecializationsComponent? known) || !TryComp(user, out CFCombatantComponent? combat))
            return null;

        var group = GroupOf(weapon);
        foreach (var rule in known.Rules)
        {
            if (Fits(user, target, combat, rule, weapon, group, melee))
                return rule.As ?? group;
        }

        return null;
    }

    private bool Fits(
        EntityUid user,
        EntityUid target,
        CFCombatantComponent combat,
        CFCriticalSpecialization rule,
        CFStrikeWeaponStats weapon,
        CFWeaponGroup group,
        bool melee)
    {
        if (combat.Level < rule.MinLevel
            || rule.Groups.Count > 0 && !rule.Groups.Contains(group)
            || rule.AgileOrFinesse && !weapon.Agile && !weapon.Finesse
            || rule.MeleeOnly && !melee
            || rule.UnarmedOnly && !weapon.Traits.Unarmed
            || rule.InnovationOnly && (weapon.Item is not { } held || !HasComp<CFInnovationComponent>(held)))
        {
            return false;
        }

        if ((rule.Weapons.Count > 0 || rule.WeaponTraits.Count > 0) && !IsListed(rule, weapon))
            return false;

        if (rule.SneakAttackWeapon
            && (weapon.Traits.Unarmed || weapon.DamageDie > (weapon.Proficiency == CFWeaponCategory.Simple.Proficiency() ? 8 : 6)))
        {
            return false;
        }

        if (rule.MinRank > CFProficiencyRank.Untrained && _weapons.Rank(combat, weapon) < rule.MinRank)
            return false;

        var style = CompOrNull<CFCombatOptionsComponent>(user);
        return rule.When switch
        {
            CFCriticalSpecializationWhen.Raging => style is { Raging: true },
            CFCriticalSpecializationWhen.TargetOffGuard => _flanking.IsOffGuardTo(target, user) || _conditions.IsOffGuardTo(target, user),
            CFCriticalSpecializationWhen.TargetIsPrey => style != null && style.Prey == target,
            _ => true,
        };
    }

    private bool IsListed(CFCriticalSpecialization rule, CFStrikeWeaponStats weapon)
    {
        if (weapon.Item is not { } item)
            return false;

        if (MetaData(item).EntityPrototype is { } proto && rule.Weapons.Contains(proto.ID))
            return true;

        return rule.WeaponTraits.Count > 0 && TryComp(item, out CFTraitComponent? traits) && traits.HasAny(rule.WeaponTraits);
    }

    /// <summary>
    /// Runs the critical specialization effect of a critical hit, if the attacker has one for it. True if it had one.
    /// </summary>
    /// <param name="attackTotal">The attack roll's result, which an axe compares with a second creature's AC.</param>
    public bool TryApply(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, bool melee, int attackTotal)
    {
        if (Applies(user, target, weapon, melee) is not { } group)
            return false;

        Apply(user, target, weapon, group, attackTotal);
        return true;
    }

    /// <summary>What a weapon group does to the creature it critically hit.</summary>
    public void Apply(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, CFWeaponGroup group, int attackTotal)
    {
        var dc = CFCasting.ClassDc(_prototypes, CompOrNull<CFCombatantComponent>(user), CompOrNull<CFAbilityScoreComponent>(user)).Dc;
        switch (group)
        {
            case CFWeaponGroup.Sword:
                _conditions.ApplyCondition(target, CFConditionKind.OffGuard, 0, Seconds, user, 0);
                break;
            case CFWeaponGroup.Spear:
                _conditions.ApplyCondition(target, CFConditionKind.Clumsy, 1, Seconds, user, 0);
                break;
            case CFWeaponGroup.Hammer:
                if (!_conditions.RollDefense(target, CFDefenseType.Fortitude, dc, out _).IsSuccess())
                    _conditions.KnockProne(target);
                break;
            case CFWeaponGroup.Flail:
                if (!_conditions.RollDefense(target, CFDefenseType.Reflex, dc, out _).IsSuccess())
                    _conditions.KnockProne(target);
                break;
            case CFWeaponGroup.Brawling:
                if (!_conditions.RollDefense(target, CFDefenseType.Fortitude, dc, out _).IsSuccess())
                    _bonuses.SetActionCap(target, 2, Seconds);
                break;
            case CFWeaponGroup.Firearm:
            case CFWeaponGroup.Sling:
                if (!_conditions.RollDefense(target, CFDefenseType.Fortitude, dc, out _).IsSuccess())
                    _conditions.ApplyCondition(target, CFConditionKind.Stunned, 1, 0f, user, 0);
                break;
            case CFWeaponGroup.Knife:
            case CFWeaponGroup.Dart:
                _bonuses.AddPersistent(target, "Bleed", 1, 6, weapon.Potency, user);
                break;
            case CFWeaponGroup.Crossbow:
                _bonuses.AddPersistent(target, "Bleed", 1, 8, weapon.Potency, user);
                break;
            case CFWeaponGroup.Pick:
                _effects.DealDamage(user, target, 2 * weapon.DamageDice, weapon.DamageType, critical: false);
                break;
            case CFWeaponGroup.Club:
                _conditions.Push(user, target, 10);
                break;
            case CFWeaponGroup.Polearm:
            case CFWeaponGroup.Shield:
                _conditions.Push(user, target, 5);
                break;
            case CFWeaponGroup.Bow:
                if (_lookup.GetEntitiesInRange<WallComponent>(Transform(target).Coordinates, PinRange).Count > 0)
                    _conditions.ApplyCondition(target, CFConditionKind.Immobilized, 0, Seconds, user, 0);
                break;
            case CFWeaponGroup.Axe:
                Cleave(user, target, weapon, attackTotal);
                break;
            default:
                return;
        }

        _popup.PopupEntity(Loc.GetString("cf-critical-specialization", ("group", Loc.GetString($"cf-weapon-group-{group.ToString().ToLowerInvariant()}"))), target);
    }

    /// <summary>
    /// An axe carries on into a second enemy beside the first and within the attacker's reach, if the attack roll beats its
    /// AC. It takes the weapon's damage dice alone, rolled again here: nothing is added and nothing is doubled.
    /// </summary>
    private void Cleave(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, int attackTotal)
    {
        // Half a tile more than the reach circle, for the second creature's own body.
        var reach = CFFlanking.ReachInUnits(Math.Max(5, weapon.Traits.Reach)) + 0.5f;
        var from = _transform.GetWorldPosition(user);
        var at = _transform.GetWorldPosition(target);
        EntityUid? second = null;
        var best = float.MaxValue;
        foreach (var (uid, combat) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(target).Coordinates, 1.5f))
        {
            if (uid == target || uid == user || !_mobState.IsAlive(uid) || !_factions.CanHarm(user, uid))
                continue;

            var position = _transform.GetWorldPosition(uid);
            var away = (position - at).Length();
            if ((position - from).Length() > reach || away >= best)
                continue;

            if (combat.ArmorClass + _conditions.ArmorClassModifier(uid) >= attackTotal)
                continue;

            best = away;
            second = uid;
        }

        if (second is not { } hit)
            return;

        var damage = 0;
        for (var i = 0; i < weapon.DamageDice; i++)
        {
            damage += _random.Next(1, weapon.DamageDie + 1);
        }

        _effects.DealDamage(user, hit, damage, weapon.DamageType, critical: false);
    }
}
