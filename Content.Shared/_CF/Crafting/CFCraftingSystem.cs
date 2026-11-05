using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Crafting;

/// <summary>Crafting checks both sides need.</summary>
public sealed partial class CFCraftingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public CFProficiencyRank Rank(EntityUid user)
    {
        return TryComp(user, out CFCombatantComponent? combat)
            ? CFDefense.SkillRank(_prototypes, combat, CFCraftingRules.Skill)
            : CFProficiencyRank.Untrained;
    }

    /// <summary>
    /// Whether the stock construction menu works for this entity. Characters need training in Crafting.
    /// Anything without PF2e stats, such as an admin ghost, keeps it.
    /// </summary>
    public bool CanUseConstructionMenu(EntityUid user)
    {
        return !HasComp<CFCombatantComponent>(user) || Rank(user) >= CFProficiencyRank.Trained;
    }
}
