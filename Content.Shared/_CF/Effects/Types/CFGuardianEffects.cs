using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Raise Haft: +1 circumstance to AC for a moment with a two-handed melee weapon, or +2 when the weapon has the parry
/// trait of its own.
/// </summary>
public sealed partial class CFRaiseHaftEffect : CFEffect
{
    public static readonly ProtoId<CFTraitPrototype> Parry = "Parry";

    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var bonus = 1;
        foreach (var held in entities.System<SharedHandsSystem>().EnumerateHeld(args.User))
        {
            if (entities.TryGetComponent(held, out CFTraitComponent? traits) && traits.HasTrait(Parry))
                bonus = 2;
        }

        entities.System<CFBonusSystem>().AddBonus(args.User, CFBonusStat.ArmorClass, CFBonusType.Circumstance, bonus, DurationSeconds);
    }
}

/// <summary>
/// Shielded Attrition: every enemy in reach of the user spends its reaction, so none is left for the user's allies. The
/// rules provoke them with a Stride; here the Guardian's show of defiance is enough.
/// </summary>
public sealed partial class CFSpendEnemyReactionsEffect : CFEffect
{
    [DataField]
    public int Feet = 10;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var lookup = entities.System<EntityLookupSystem>();
        var factions = entities.System<CFFactionSystem>();
        var conditions = entities.System<CFConditionSystem>();
        var coordinates = entities.GetComponent<TransformComponent>(args.User).Coordinates;
        foreach (var enemy in lookup.GetEntitiesInRange<CFCombatOptionsComponent>(coordinates, Feet / CFMelee.FeetPerTile))
        {
            if (enemy.Owner == args.User || !factions.CanHarm(args.User, enemy))
                continue;

            if (enemy.Comp.ReactionSpent == 0)
            {
                enemy.Comp.ReactionSpent = CFReactions.Granted;
                entities.Dirty(enemy.Owner, enemy.Comp);
            }
        }
    }
}
