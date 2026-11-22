using Content.Shared.CF.Checks;
using Content.Shared.CF.Traits;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// One piece of what a spell, bomb, trap, aura, or rune does to a creature or a point.
/// Lists of these sit on <see cref="CFDegreeEffectsComponent"/> and run once per affected target.
/// </summary>
/// <remarks>
/// Modeled on crystall-punk-14's <c>CP14SpellEffect</c>, but resolved by Pathfinder 2e degree of success.
/// Effects are data. They must not keep state between calls: anything that lasts goes on a component.
/// Effects run on the server only.
/// </remarks>
[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class CFEffect
{
    public abstract void Apply(IEntityManager entities, CFEffectArgs args);
}

/// <summary>
/// Everything an effect knows about the cast or activation that triggered it.
/// </summary>
public sealed record CFEffectArgs
{
    /// <summary>The creature casting the spell, throwing the bomb, or springing the trap.</summary>
    public EntityUid User;

    /// <summary>The spell action, item, or hazard whose components describe the effect.</summary>
    public EntityUid Source;

    /// <summary>The creature this run applies to. Null for effects aimed at a point.</summary>
    public EntityUid? Target;

    /// <summary>The target's position, or the aimed point for area and nested effects.</summary>
    public EntityCoordinates? Position;

    /// <summary>Degree of the check, from the roller's side: the attacker for attacks, the target for saves.</summary>
    public CFDegree Degree = CFDegree.Success;

    /// <summary>What was rolled. <see cref="CFDefenseType.None"/> when the source has no check.</summary>
    public CFDefenseType Defense = CFDefenseType.None;

    public bool BasicSave;

    /// <summary>The source's traits: what a target is rolling against, such as Fear or Poison.</summary>
    public HashSet<ProtoId<CFTraitPrototype>> Traits = new();

    /// <summary>Rank the spell resolves at. Cantrips are half the caster's level, rounded up.</summary>
    public int Rank;

    /// <summary>Times the spell has heightened past its base rank (see <see cref="CFHeightenComponent"/>).</summary>
    public int HeightenSteps;

    /// <summary>The DC targets saved against, also used by sanctuary and other DC-based riders.</summary>
    public int Dc;

    /// <summary>The user's spellcasting or key ability modifier, for effects that add it.</summary>
    public int AbilityModifier;
}
