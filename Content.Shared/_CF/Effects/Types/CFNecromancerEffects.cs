using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Shared.CF.Effects.Types;

/// <summary>Consume Thrall: a thrall within <see cref="Feet"/> is destroyed and the user regains Focus Points.</summary>
public sealed partial class CFConsumeThrallEffect : CFEffect
{
    [DataField]
    public int Feet = 30;

    [DataField]
    public int FocusPoints = 1;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var transform = entities.System<SharedTransformSystem>();
        if (entities.System<CFThrallSystem>().Nearest(args.User, transform.GetMapCoordinates(args.User), Feet) is not { } thrall
            || !entities.TryGetComponent(args.User, out CFSpellbookComponent? book))
        {
            return;
        }

        entities.QueueDeleteEntity(thrall);
        book.FocusSpent = Math.Max(0, book.FocusSpent - FocusPoints);
        entities.Dirty(args.User, book);
    }
}

/// <summary>
/// Draining Strike: a thrall within <see cref="Feet"/> of the user or the target is destroyed, the target takes a die of
/// spirit damage more, and the user regains as many Hit Points.
/// </summary>
public sealed partial class CFDrainThrallEffect : CFEffect
{
    [DataField]
    public int Feet = 10;

    [DataField]
    public int Die = 6;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var transform = entities.System<SharedTransformSystem>();
        var thralls = entities.System<CFThrallSystem>();
        var thrall = thralls.Nearest(args.User, transform.GetMapCoordinates(args.User), Feet)
                     ?? thralls.Nearest(args.User, transform.GetMapCoordinates(target), Feet);
        if (thrall == null)
            return;

        entities.QueueDeleteEntity(thrall.Value);
        var system = entities.System<CFEffectSystem>();
        var amount = system.Roll(1, Die, 0);
        system.DealDamage(args.User, target, amount, "Spirit", false);
        system.Heal(args.User, args.User, amount);
    }
}

/// <summary>
/// Song of the Soul, cast on a thrall: the nearest hurt ally within <see cref="Feet"/> regains Hit Points at once, and the
/// thrall goes on healing that creature every 6 seconds while it stays within <see cref="Feet"/>.
/// </summary>
public sealed partial class CFSongOfTheSoulEffect : CFEffect
{
    [DataField]
    public int Feet = 15;

    [DataField]
    public int Dice = 1;

    [DataField]
    public int Die = 8;

    [DataField]
    public int HeightenDice = 1;

    /// <summary>Healing every 6 seconds, and one more per rank.</summary>
    [DataField]
    public int FastHealing = 1;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } thrall || !entities.TryGetComponent(thrall, out CFThrallComponent? comp))
            return;

        var transform = entities.System<SharedTransformSystem>();
        var factions = entities.System<Content.Shared.CF.Factions.CFFactionSystem>();
        var origin = transform.GetWorldPosition(thrall);
        var mapId = transform.GetMapId(thrall);
        var reach = Feet / CFMelee.FeetPerTile + 0.5f;
        EntityUid? best = null;
        var bestDistance = reach * reach;
        var query = entities.EntityQueryEnumerator<CFCombatantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var combat, out var xform))
        {
            if (uid == thrall || combat.CurrentHp <= 0 || combat.CurrentHp >= combat.MaxHp || entities.HasComponent<CFThrallComponent>(uid))
                continue;

            if (xform.MapID != mapId || factions.CanHarm(args.User, uid))
                continue;

            var distance = (transform.GetWorldPosition(xform) - origin).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        if (best is not { } ally)
            return;

        var system = entities.System<CFEffectSystem>();
        system.Heal(args.User, ally, system.Roll(Dice + HeightenDice * args.HeightenSteps, Die, 0));
        comp.SongFor = ally;
        comp.SongHealing = FastHealing + args.HeightenSteps;
    }
}

/// <summary>
/// Osteo Armaments: a +1 striking weapon of bone in the user's hand for an hour. The mode picks which weapon, and the next
/// use destroys the last one.
/// </summary>
public sealed partial class CFOsteoArmamentsEffect : CFEffect
{
    /// <summary>The weapon for each mode value. Mode 0, with no wheel, takes the first.</summary>
    [DataField(required: true)]
    public Dictionary<byte, EntProtoId> Weapons = new();

    [DataField]
    public float Seconds = 3600f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (Weapons.Count == 0)
            return;

        if (!Weapons.TryGetValue(args.Mode, out var prototype))
        {
            foreach (var first in Weapons.Values)
            {
                prototype = first;
                break;
            }
        }

        var necromancer = entities.EnsureComponent<CFNecromancerComponent>(args.User);
        if (necromancer.BoneWeapon is { } old && !entities.Deleted(old))
            entities.QueueDeleteEntity(old);

        var weapon = entities.SpawnAttachedTo(prototype, entities.GetComponent<TransformComponent>(args.User).Coordinates);
        var runes = entities.EnsureComponent<CFWeaponRunesComponent>(weapon);
        runes.Potency = Math.Max(runes.Potency, 1);
        runes.Striking = Math.Max(runes.Striking, 1);
        entities.Dirty(weapon, runes);
        entities.EnsureComponent<TimedDespawnComponent>(weapon).Lifetime = Seconds;
        necromancer.BoneWeapon = weapon;
        entities.System<SharedHandsSystem>().TryPickupAnyHand(args.User, weapon, animate: false);
    }
}
