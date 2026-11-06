using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Traits;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Reads a weapon entity into <see cref="CFStrikeWeaponStats"/>: traits from <c>CFTrait</c>,
/// runes from <see cref="CFWeaponRunesComponent"/>, and the two-hand die while wielded.
/// </summary>
public sealed partial class CFWeaponSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public CFWeaponTraits Traits(EntityUid weapon)
    {
        return TryComp(weapon, out CFTraitComponent? traits)
            ? CFWeaponTraits.From(_prototypes, traits.Traits)
            : CFWeaponTraits.None;
    }

    /// <summary>
    /// Strike stats for a weapon. <paramref name="thrown"/> uses the thrown range increment instead of melee.
    /// Returns false when the entity has no <see cref="CFWeaponComponent"/>.
    /// </summary>
    public bool TryStats(EntityUid weapon, out CFStrikeWeaponStats stats, bool thrown = false)
    {
        stats = default;
        if (!TryComp(weapon, out CFWeaponComponent? data))
            return false;

        var traits = Traits(weapon);
        var runes = CompOrNull<CFWeaponRunesComponent>(weapon);
        var striking = Math.Clamp(runes?.Striking ?? 0, 0, 3);

        var die = data.DamageDie;
        if (traits.TwoHand > 0 && TryComp(weapon, out WieldableComponent? wield) && wield.Wielded)
            die = traits.TwoHand;

        var ranged = TryComp(weapon, out CFRangedWeaponComponent? shooter) && !thrown;
        var increment = thrown ? traits.Thrown : shooter?.RangeIncrementFeet ?? 0;

        stats = new CFStrikeWeaponStats(
            data.DamageDice + striking,
            die,
            data.DamageType,
            data.Category.Proficiency(),
            traits,
            Ranged: ranged || thrown,
            RangeIncrementFeet: increment,
            Potency: Math.Clamp(runes?.Potency ?? 0, 0, 3),
            Striking: striking,
            Reload: ranged ? shooter!.Reload : 0,
            ThrownStrike: thrown,
            Improvised: data.Improvised,
            Item: weapon);
        return true;
    }

    /// <summary>
    /// A creature's proficiency for a Strike: its rank with the weapon's category or group, whichever is better.
    /// A familiar weapon also uses the rank of the simpler category it counts as.
    /// </summary>
    public CFProficiencyRank Rank(CFCombatantComponent combat, CFStrikeWeaponStats weapon)
    {
        var rank = CFDefense.AttackRank(_prototypes, combat, weapon.Proficiency);
        if (weapon.Item is not { } item || !TryComp(item, out CFWeaponComponent? data))
            return rank;

        rank = CFDefense.Best(combat, data.Group.ToString(), rank);
        var counts = data.Category;
        if (MetaData(item).EntityPrototype is { } proto && combat.FamiliarWeapons.Contains(proto.ID))
        {
            counts = CFWeaponCategory.Simple;
            rank = rank < CFProficiencyRank.Trained ? CFProficiencyRank.Trained : rank;
        }
        else if (IsFamiliarByTrait(combat, item))
        {
            counts = counts == CFWeaponCategory.Advanced ? CFWeaponCategory.Martial : CFWeaponCategory.Simple;
        }

        if (counts >= data.Category)
            return rank;

        var simpler = CFDefense.AttackRank(_prototypes, combat, counts.Proficiency());
        return simpler > rank ? simpler : rank;
    }

    private bool IsFamiliarByTrait(CFCombatantComponent combat, EntityUid item)
    {
        return combat.FamiliarWeaponTraits.Count > 0
               && TryComp(item, out CFTraitComponent? traits)
               && traits.HasAny(combat.FamiliarWeaponTraits);
    }

    /// <summary>Thrown range increment in feet. 0 when this is not a thrown weapon.</summary>
    public int ThrownIncrement(EntityUid item)
    {
        return HasComp<CFWeaponComponent>(item) ? Traits(item).Thrown : 0;
    }

    /// <summary>
    /// How far an item can be thrown, in tiles. A thrown weapon reaches its last range increment, the same limit
    /// a ranged Strike has. Anything else keeps <paramref name="handRange"/>.
    /// </summary>
    public float ThrowRange(EntityUid item, float handRange)
    {
        var increment = ThrownIncrement(item);
        return increment > 0 ? increment * CFMelee.MaxRangeIncrements / CFMelee.FeetPerTile : handRange;
    }

    /// <summary>
    /// Whether a right-click with this item is a maneuver instead of a wide swing: a shield, or a weapon
    /// with the shove or disarm trait. The sweep trait keeps its wide swing.
    /// </summary>
    public bool ManeuverOnRightClick(EntityUid weapon)
    {
        if (HasComp<CFShieldComponent>(weapon))
            return true;

        var traits = Traits(weapon);
        return !traits.Sweep && (traits.Shove || traits.Disarm);
    }

    /// <summary>Melee reach in feet: the weapon's own reach, raised by the reach trait.</summary>
    public int ReachFeet(EntityUid weapon)
    {
        var reach = CompOrNull<CFMeleeWeaponComponent>(weapon)?.ReachFeet ?? CFFlanking.DefaultReachFeet;
        return Math.Max(reach, Traits(weapon).Reach);
    }

    /// <summary>The property runes that work: a weapon holds as many as its potency.</summary>
    public IEnumerable<CFPropertyRunePrototype> PropertyRunes(EntityUid weapon)
    {
        if (!TryComp(weapon, out CFWeaponRunesComponent? runes))
            yield break;

        var slots = Math.Clamp(runes.Potency, 0, 3);
        foreach (var id in runes.Property)
        {
            if (slots-- <= 0)
                yield break;

            if (_prototypes.TryIndex(id, out var rune))
                yield return rune;
        }
    }
}
