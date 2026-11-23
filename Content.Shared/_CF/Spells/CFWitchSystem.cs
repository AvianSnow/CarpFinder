using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;

namespace Content.Shared.CF.Spells;

/// <summary>Who a familiar gift goes to.</summary>
public enum CFFamiliarGiftTarget : byte
{
    /// <summary>The most hurt of the Witch and the Witch's allies near the familiar.</summary>
    Ally,

    /// <summary>The nearest enemy to the familiar.</summary>
    Enemy,

    /// <summary>The familiar itself, for something that happens where it stands.</summary>
    Familiar,
}

/// <summary>What a patron's familiar does each time its Witch casts a hex.</summary>
[DataDefinition]
public sealed partial class CFFamiliarGift
{
    [DataField]
    public CFFamiliarGiftTarget Target = CFFamiliarGiftTarget.Ally;

    /// <summary>How near the familiar the creature has to be.</summary>
    [DataField]
    public int RangeFeet = 15;

    [DataField(required: true)]
    public List<CFEffect> Effects = new();
}

/// <summary>A Witch's familiar gifts. Server-only: they run when a hex is cast.</summary>
[RegisterComponent]
public sealed partial class CFWitchComponent : Component
{
    [DataField]
    public List<CFFamiliarGift> Gifts = new();
}

/// <summary>Raised on a caster who has just paid for a hex.</summary>
[ByRefEvent]
public readonly record struct CFHexCastEvent(EntityUid Spell);

/// <summary>
/// Runs a Witch's familiar gifts when a hex is cast. The rules also give them each time a hex is sustained: here a
/// sustained spell keeps itself going for free, so only the cast counts.
/// </summary>
public sealed partial class CFWitchSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnHexCast(Entity<CFWitchComponent> ent, ref CFHexCastEvent args)
    {
        if (!_net.IsServer || Familiar(ent) is not { } familiar)
            return;

        foreach (var gift in ent.Comp.Gifts)
        {
            if (GiftTarget(ent, familiar, gift) is not { } target)
                continue;

            _effects.Run(gift.Effects, new CFEffectArgs
            {
                User = ent,
                Source = familiar,
                Target = target,
                Position = Transform(target).Coordinates,
            });
        }
    }

    /// <summary>The Witch's living familiar: a companion bound to the Witch that does not fight.</summary>
    public EntityUid? Familiar(EntityUid witch)
    {
        var query = EntityQueryEnumerator<CFCompanionComponent>();
        while (query.MoveNext(out var uid, out var companion))
        {
            if (companion.Summoner == witch && companion.Peaceful && _mobState.IsAlive(uid))
                return uid;
        }

        return null;
    }

    private EntityUid? GiftTarget(EntityUid witch, EntityUid familiar, CFFamiliarGift gift)
    {
        if (gift.Target == CFFamiliarGiftTarget.Familiar)
            return familiar;

        var origin = _transform.GetMapCoordinates(familiar);
        var range = gift.RangeFeet / 5f;
        EntityUid? best = null;
        var bestScore = float.MaxValue;
        foreach (var uid in _lookup.GetEntitiesInRange<Content.Shared.CF.Combat.CFCombatantComponent>(origin, range))
        {
            if (uid.Owner == familiar || !_mobState.IsAlive(uid))
                continue;

            var hostile = uid.Owner != witch && _factions.CanHarm(witch, uid);
            if (hostile != (gift.Target == CFFamiliarGiftTarget.Enemy))
                continue;

            // An ally by how hurt it is, an enemy by how close it is.
            var score = hostile
                ? (_transform.GetMapCoordinates(uid).Position - origin.Position).Length()
                : uid.Comp.CurrentHp / (float) Math.Max(1, uid.Comp.MaxHp);
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = uid;
        }

        return best;
    }
}

/// <summary>Nudge Fate: the next save or skill check the target would just fail gets +1 status, once, within the time.</summary>
public sealed partial class CFNudgeFateEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<Content.Shared.CF.Combat.CFConditionSystem>().NudgeFate(target, DurationSeconds);
    }
}
