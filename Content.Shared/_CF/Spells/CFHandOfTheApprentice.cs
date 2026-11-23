using Content.Shared.Actions.Events;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;

namespace Content.Shared.CF.Spells;

/// <summary>The action cannot be used without a melee weapon in hand (Hand of the Apprentice).</summary>
[RegisterComponent]
public sealed partial class CFNeedsMeleeWeaponComponent : Component
{
}

/// <summary>Refuses actions that need a held melee weapon before anything is spent.</summary>
public sealed partial class CFNeedsMeleeWeaponSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFNeedsMeleeWeaponComponent> ent, ref ActionAttemptEvent args)
    {
        if (HeldMeleeWeapon(args.User) != null)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-stance-needs-melee");
    }

    /// <summary>The first melee weapon the creature holds, or null.</summary>
    public EntityUid? HeldMeleeWeapon(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFMeleeWeaponComponent>(held))
                return held;
        }

        return null;
    }
}

/// <summary>
/// Hand of the Apprentice: the held melee weapon flies at the target and back. On a hit it deals the weapon's damage with the
/// spellcasting modifier in place of Strength, doubled on a critical hit. The spell attack is the source's <c>CFCheck</c>.
/// </summary>
public sealed partial class CFHurledWeaponEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target
            || entities.System<CFNeedsMeleeWeaponSystem>().HeldMeleeWeapon(args.User) is not { } weapon
            || !entities.System<CFWeaponSystem>().TryStats(weapon, out var stats))
        {
            return;
        }

        var system = entities.System<CFEffectSystem>();
        var multiplier = CFEffectSystem.DamageMultiplier(args);
        if (multiplier <= 0f)
        {
            system.ShowMiss(target, stats.DamageType);
            return;
        }

        var rolled = system.Roll(stats.DamageDice, stats.DamageDie, args.AbilityModifier + stats.Potency);
        var amount = multiplier < 1f ? (int) (rolled * multiplier) : rolled * (int) multiplier;
        system.DealDamage(args.User, target, amount, stats.DamageType, multiplier >= 2f);
    }
}

/// <summary>The target takes a penalty to attack rolls against the user for 6 seconds (Charming Push), or longer (Wilding Word).</summary>
public sealed partial class CFOverextendEffect : CFEffect
{
    [DataField]
    public int Penalty = 2;

    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target && target != args.User)
            entities.System<CFConditionSystem>().Overextend(target, args.User, once: false, Penalty, DurationSeconds);
    }
}

/// <summary>Drain Bonded Item: see <see cref="CFSpellSystem.DrainBondedItem"/>.</summary>
public sealed partial class CFDrainBondedItemEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFSpellSystem>().DrainBondedItem(args.User);
    }
}
