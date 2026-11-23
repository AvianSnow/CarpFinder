using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Companions;

/// <summary>A Necromancer's thrall. It fights for its Necromancer on a GOAP brain, and falls apart after a minute.</summary>
[RegisterComponent]
public sealed partial class CFThrallComponent : Component
{
    [DataField]
    public EntityUid Necromancer;

    [DataField]
    public TimeSpan Expires;

    /// <summary>Until this time, the thrall is carrying out an order.</summary>
    [DataField]
    public TimeSpan OrderUntil;

    /// <summary>Extra d6s on the thrall's next hits, until <see cref="OrderUntil"/> (Thrall Charge).</summary>
    [DataField]
    public int ChargeDice;

    /// <summary>Song of the Soul: the creature the thrall's silent song heals while it stays within 15 feet.</summary>
    [DataField]
    public EntityUid? SongFor;

    /// <summary>What the song heals every 6 seconds.</summary>
    [DataField]
    public int SongHealing;

    [DataField]
    public TimeSpan NextSong;
}

/// <summary>An action that sends one thrall at the target. The server runs it.</summary>
[RegisterComponent]
public sealed partial class CFCommandThrallActionComponent : Component
{
}

/// <summary>What a Necromancer's thralls are like, set by their class choices.</summary>
[RegisterComponent]
public sealed partial class CFNecromancerComponent : Component
{
    /// <summary>Thralls made per casting of Create Thrall, beyond the usual two.</summary>
    [DataField]
    public int ExtraThralls;

    /// <summary>The Necromancer regains Hit Points when a thrall is destroyed.</summary>
    [DataField]
    public bool BloodReturns;

    /// <summary>Added to each thrall's Speed, in feet.</summary>
    [DataField]
    public int ExtraSpeed;

    /// <summary>Replaces a thrall's damage type. Empty keeps it.</summary>
    [DataField]
    public string DamageType = string.Empty;

    /// <summary>Thrall Teamwork: a free melee Strike after Create Thrall on an enemy in reach beside a thrall.</summary>
    [DataField]
    public bool Teamwork;

    /// <summary>Mobile Thralls: every thrall has a climb Speed.</summary>
    [DataField]
    public bool Climb;

    /// <summary>A trait every thrall gets (Holy, Unholy). Empty gives none.</summary>
    [DataField]
    public string Trait = string.Empty;

    /// <summary>Spirit damage added to every thrall's hit (The Hallowed Dead, The Unholy Dead).</summary>
    [DataField]
    public int SpiritDamage;

    /// <summary>The Flesh fascination: a destroyed thrall leaves difficult terrain for 10 minutes.</summary>
    [DataField]
    public bool LeavesTerrain;

    /// <summary>The bone weapon Osteo Armaments made last, which the next use destroys.</summary>
    [DataField]
    public EntityUid? BoneWeapon;
}

/// <summary>
/// Raised on a caster to make thralls. Only the server can bind a companion, so the server answers it. <paramref name="At"/>
/// is where they rise, beside the caster when null, and <paramref name="Teamwork"/> allows the Reaper's free Strike.
/// </summary>
[ByRefEvent]
public readonly record struct CFCreateThrallsEvent(int Count, EntityCoordinates? At = null, bool Teamwork = true);

/// <summary>Raised on a caster to send a thrall at an enemy.</summary>
[ByRefEvent]
public readonly record struct CFChargeThrallEvent(EntityUid Target, int Dice);

/// <summary>Create Thrall.</summary>
public sealed partial class CFCreateThrallsEffect : CFEffect
{
    [DataField]
    public int Count = 2;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var ev = new CFCreateThrallsEvent(Count);
        entities.EventBus.RaiseLocalEvent(args.User, ref ev);
    }
}

/// <summary>Thrall Charge: sends the nearest thrall at the target, hitting harder.</summary>
public sealed partial class CFChargeThrallEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var ev = new CFChargeThrallEvent(target, args.Rank >= 2 ? 2 : 1);
        entities.EventBus.RaiseLocalEvent(args.User, ref ev);
    }
}

/// <summary>Heals the caster, for an effect that lands on someone else (Life Tap).</summary>
public sealed partial class CFHealUserEffect : CFEffect
{
    [DataField]
    public int PerLevel;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var level = entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        entities.System<CFEffectSystem>().Heal(args.User, args.User, PerLevel * level);
    }
}

/// <summary>
/// A grave spell. It is cast on one of the caster's thralls, and affects the creatures around that thrall. Each one rolls
/// <see cref="Defense"/> against the spell DC and takes the lists for how that went.
/// </summary>
public sealed partial class CFThrallBlastEffect : CFEffect
{
    [DataField(required: true)]
    public CFAreaShape Shape;

    [DataField(required: true)]
    public int SizeFeet;

    /// <summary>0 means everyone in the area.</summary>
    [DataField]
    public int MaxTargets;

    [DataField]
    public CFDefenseType Defense = CFDefenseType.Fortitude;

    [DataField]
    public bool BasicSave = true;

    /// <summary>False if the thrall survives.</summary>
    [DataField]
    public bool Destroys = true;

    /// <summary>Who the blast reaches. Muscle Barrier reaches allies.</summary>
    [DataField]
    public CFTargetFilter Filter = CFTargetFilter.Enemies;

    /// <summary>The caster is a creature the blast can reach.</summary>
    [DataField]
    public bool IncludesUser;

    /// <summary>Nobody rolls: <see cref="Effects"/> run on everyone reached, as a success.</summary>
    [DataField]
    public bool NoCheck;

    [DataField]
    public List<CFEffect> Effects = new();

    [DataField]
    public List<CFEffect> Success = new();

    [DataField]
    public List<CFEffect> Failure = new();

    [DataField]
    public List<CFEffect> CriticalFailure = new();

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } thrall)
            entities.System<CFThrallSystem>().Blast(this, args, thrall);
    }
}

/// <summary>What is true of thralls on both sides, and grave spells.</summary>
public sealed partial class CFThrallSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;

    /// <summary>The target of a grave spell: the nearest enemy to the thrall, for the direction of a line or cone.</summary>
    private MapCoordinates Aim(EntityUid user, EntityUid thrall, MapCoordinates origin)
    {
        var best = origin;
        var bestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != origin.MapId || uid == thrall || !_factions.CanHarm(user, uid))
                continue;

            var position = _transform.GetWorldPosition(uid);
            var distance = (position - origin.Position).LengthSquared();
            if (distance > 0.01f && distance < bestDistance)
            {
                bestDistance = distance;
                best = new MapCoordinates(position, origin.MapId);
            }
        }

        return best;
    }

    public void Blast(CFThrallBlastEffect blast, CFEffectArgs args, EntityUid thrall)
    {
        if (!_net.IsServer)
            return;

        var origin = _transform.GetMapCoordinates(thrall);
        var atPoint = blast.Shape is CFAreaShape.Burst or CFAreaShape.Square;
        var aim = atPoint ? origin : Aim(args.User, thrall, origin);

        var found = _effects.CollectArea(args.User, blast.Shape, blast.SizeFeet, origin, aim, atPoint, includeCenter: true, selfAllowed: blast.IncludesUser, blast.Filter, null, blast.MaxTargets);
        foreach (var creature in found)
        {
            if (creature == thrall)
                continue;

            var hit = _effects.BuildArgs(args.Source, args.User, creature, Transform(creature).Coordinates, null, out var stats);
            hit.Defense = blast.Defense;
            hit.BasicSave = blast.BasicSave;
            hit.Degree = blast.NoCheck ? CFDegree.Success : _conditions.RollDefense(creature, blast.Defense, stats.Dc, out _, args.Traits);
            _effects.Run(blast.Effects, hit);
            if (blast.NoCheck)
                continue;

            _effects.Run(hit.Degree switch
                {
                    CFDegree.Success => blast.Success,
                    CFDegree.Failure => blast.Failure,
                    CFDegree.CriticalFailure => blast.CriticalFailure,
                    _ => NoEffects,
                },
                hit);
        }

        if (blast.Destroys)
            QueueDel(thrall);
    }

    private static readonly List<CFEffect> NoEffects = new();

    private static readonly EntProtoId FleshTerrain = "CFFillFleshTerrain";

    /// <summary>The living thrall of <paramref name="owner"/> nearest to <paramref name="origin"/> within <paramref name="feet"/>, or null.</summary>
    public EntityUid? Nearest(EntityUid owner, MapCoordinates origin, int feet)
    {
        EntityUid? best = null;
        var bestDistance = feet / CFMelee.FeetPerTile + 0.5f;
        bestDistance *= bestDistance;
        var query = EntityQueryEnumerator<CFThrallComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thrall, out var xform))
        {
            if (thrall.Necromancer != owner || xform.MapID != origin.MapId || TerminatingOrDeleted(uid))
                continue;

            if (CompOrNull<CFCombatantComponent>(uid) is { CurrentHp: <= 0 })
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>A thrall is going. It is still where it stood, which a component shutdown is not.</summary>
    [SubscribeLocalEvent]
    private void OnTerminating(Entity<CFThrallComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_net.IsServer
            || TerminatingOrDeleted(ent.Comp.Necromancer)
            || CompOrNull<CFNecromancerComponent>(ent.Comp.Necromancer) is not { LeavesTerrain: true }
            || Transform(ent).GridUid == null)
        {
            return;
        }

        // The Flesh fascination: what is left of the thrall is hard to cross.
        SpawnAtPosition(FleshTerrain, Transform(ent).Coordinates);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<CFThrallComponent> ent, ref ComponentShutdown args)
    {
        if (!_net.IsServer
            || TerminatingOrDeleted(ent.Comp.Necromancer)
            || CompOrNull<CFNecromancerComponent>(ent.Comp.Necromancer) is not { BloodReturns: true })
        {
            return;
        }

        var level = CompOrNull<CFCombatantComponent>(ent.Comp.Necromancer)?.Level ?? 1;
        _effects.Heal(ent.Comp.Necromancer, ent.Comp.Necromancer, 1 + (level - 1) / 4);
    }
}
