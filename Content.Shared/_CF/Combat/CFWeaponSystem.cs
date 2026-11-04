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
            ThrownStrike: thrown);
        return true;
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
