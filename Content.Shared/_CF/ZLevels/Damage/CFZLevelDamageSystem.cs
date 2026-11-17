// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: the damage follows the Player Core falling rules and goes to CF hit points, because stock
// damage never reaches them (see ss14rules.md). The events a landing raises are CrystallEdge's, so a soft surface
// and the soft landing effect still ease a fall.

using Content.Shared.CF.Combat;
using Content.Shared.CF.Checks;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.ZLevels.Core;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.Effects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.ZLevels.Damage;

/// <summary>
/// Turns a hard landing into Player Core fall damage: half the distance fallen in bludgeoning damage, and the
/// creature lands prone. A creature that is landed on makes a DC 15 Reflex save against half of that damage, as a
/// basic save. Items take nothing.
/// </summary>
public sealed partial class CFZLevelDamageSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>The Reflex DC of a creature something lands on.</summary>
    public const int LandedOnDc = 15;

    private static readonly EntProtoId FallVfx = "CFDustEffect";

    [SubscribeLocalEvent]
    private void OnFallDamage(Entity<CFCombatantComponent> ent, ref CFZLevelHitEvent args)
    {
        if (_net.IsClient)
        {
            // Only the dust is predicted. The rolls and damage are the server's.
            if (_timing.IsFirstTimePredicted)
                SpawnAtPosition(FallVfx, Transform(ent).Coordinates);

            return;
        }

        var feet = CFFallRules.FeetFromImpact(args.ImpactPower, CFSharedZLevelsSystem.ZGravityForce);
        var damage = FallsSafely(ent) ? 0 : CFFallRules.Damage(FallFeet(ent, feet));

        // What the creature landed on can soften the fall, as the soft landing effect does.
        var selfEv = new CFZFallingDamageCalculateEvent(ent, args.ImpactPower);
        RaiseLocalEvent(ent, selfEv);
        var multiplier = selfEv.DamageMultiplier;

        var landedOn = _lookup.GetEntitiesInRange(ent, 0.25f, LookupFlags.Uncontained);
        landedOn.Remove(ent);

        var imFallOnEv = new CFZImFallOnEvent(landedOn, args.ImpactPower);
        RaiseLocalEvent(ent, imFallOnEv);

        var flashTargets = new List<EntityUid>();
        foreach (var victim in landedOn)
        {
            var victimEv = new CFZFallingDamageCalculateEvent(ent, args.ImpactPower);
            RaiseLocalEvent(victim, victimEv);
            // The softest thing landed on counts.
            if (MathF.Abs(victimEv.DamageMultiplier - 1f) > MathF.Abs(multiplier - 1f))
                multiplier = victimEv.DamageMultiplier;

            var fellOnMeEv = new CFZFellOnMeEvent(ent, args.ImpactPower);
            RaiseLocalEvent(victim, fellOnMeEv);

            if (damage > 0 && HasComp<CFCombatantComponent>(victim) && LandOn(ent, victim, damage))
                flashTargets.Add(victim);
        }

        damage = (int)MathF.Round(damage * multiplier);
        if (damage > 0)
        {
            _effects.DealDamage(ent, ent, damage, CFDamageTypes.Bludgeoning, false);
            _conditions.KnockProne(ent);
            flashTargets.Add(ent);
        }

        if (flashTargets.Count > 0)
            _colorFlash.RaiseEffect(Color.Red, flashTargets, CFFilter.ZPvsExcept(ent, EntityManager));
    }

    /// <summary>
    /// A creature landed on makes a basic Reflex save against half the faller's damage. Returns true if it was hurt.
    /// </summary>
    private bool LandOn(EntityUid faller, EntityUid victim, int fallerDamage)
    {
        var half = fallerDamage / 2;
        var degree = _conditions.RollDefense(victim, CFDefenseType.Reflex, LandedOnDc, out _);
        var damage = degree switch
        {
            CFDegree.CriticalSuccess => 0,
            CFDegree.Success => half / 2,
            CFDegree.Failure => half,
            _ => fallerDamage,
        };

        if (damage <= 0)
            return false;

        _effects.DealDamage(faller, victim, damage, CFDamageTypes.Bludgeoning, false);
        if (degree == CFDegree.CriticalFailure)
            _conditions.KnockProne(victim);

        return true;
    }

    /// <summary>Whether a fall does this creature no harm at all, such as a leshy's leaf glide with a free hand.</summary>
    public bool FallsSafely(EntityUid creature)
    {
        return TryComp(creature, out CFSafeFallComponent? safe)
               && !safe.HalfDistance
               && (!safe.NeedsFreeHand || _hands.CountFreeHands(creature) > 0);
    }

    /// <summary>The distance a fall counts as for damage: half for a creature that falls well.</summary>
    public int FallFeet(EntityUid creature, int feet)
    {
        return TryComp(creature, out CFSafeFallComponent? safe) && safe.HalfDistance ? feet / 2 : feet;
    }
}

/// <summary>
/// Raised on the creature that fell, and on everything it fell on. Together they say how much of the fall's damage
/// the faller takes: a soft surface or a soft landing effect lowers <see cref="DamageMultiplier"/>.
/// </summary>
public sealed partial class CFZFallingDamageCalculateEvent(EntityUid fallen, float speed) : EntityEventArgs
{
    public EntityUid Fallen = fallen;

    public float DamageMultiplier = 1;
    public float StunMultiplier = 1;
    public float Speed = speed;
}

/// <summary>Raised on a falling creature with everything it is landing on and the speed of the landing.</summary>
public sealed partial class CFZImFallOnEvent(HashSet<EntityUid> targets, float speed) : EntityEventArgs
{
    public HashSet<EntityUid> Targets = targets;
    public float Speed = speed;
}

/// <summary>Raised on something a creature lands on, with the creature and the speed of the landing.</summary>
public sealed partial class CFZFellOnMeEvent(EntityUid fallen, float speed) : EntityEventArgs
{
    public EntityUid Fallen = fallen;
    public float Speed = speed;
}
