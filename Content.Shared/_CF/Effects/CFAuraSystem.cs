using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Items;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Runs auras. It visits only creatures that have one, a few times a second, and asks the area lookup who stands in the
/// emanation, so the cost follows the number of auras and the creatures near them. A bonus goes on when a creature enters
/// and comes off when it leaves. Real time has no turns, so "at the end of your turn" is every 6 seconds.
/// </summary>
public sealed partial class CFAuraSystem : EntitySystem
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan EaseEvery = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SpiritShieldsLast = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FloorLasts = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFImplementSystem _implements = default!;
    [Dependency] private CFTacticSystem _tactics = default!;

    /// <param name="sizeFeet">A larger emanation than the aura has. 0 leaves it alone.</param>
    public void Add(EntityUid uid, IEnumerable<CFAuraEffect> effects, int sizeFeet = 0)
    {
        var aura = EnsureComp<CFAuraComponent>(uid);
        aura.Effects.AddRange(effects);
        aura.SizeFeet = Math.Max(aura.SizeFeet, sizeFeet);
    }

    /// <summary>True if a living creature is inside this creature's aura right now.</summary>
    public bool Contains(EntityUid owner, EntityUid other)
    {
        return TryComp(owner, out CFAuraComponent? aura)
               && Collect(owner, aura, _transform.GetMapCoordinates(owner), CFTargetFilter.All).Contains(other);
    }

    /// <summary>Raises a held shield and starts Shields of the Spirit. False if there is no shield to raise.</summary>
    public bool RaiseSpiritShields(EntityUid uid)
    {
        if (!TryComp(uid, out CFAuraComponent? aura) || !_shields.TryGetUsableShield(uid, out var shield))
            return false;

        _shields.SetRaised(shield, true);
        aura.SpiritShieldsUntil = _timing.CurTime + SpiritShieldsLast;
        Refresh(uid, aura);
        return true;
    }

    /// <summary>Call when <paramref name="attacker"/> makes an attack against <paramref name="target"/>, hit or miss.</summary>
    public void NoteAttacked(EntityUid target, EntityUid attacker)
    {
        if (!_net.IsServer || !TryComp(target, out CFAuraRetaliationComponent? retaliation) || !_mobState.IsAlive(attacker))
            return;

        // One extra die for every 2 ranks above the first, by the owner's focus spell rank.
        var level = CompOrNull<CFCombatantComponent>(retaliation.Owner)?.Level ?? 1;
        var dice = 1 + (CFCasting.CantripRank(level) - 1) / 2;
        var damage = 0;
        for (var i = 0; i < dice; i++)
        {
            damage += _random.Next(1, retaliation.Die + 1);
        }

        _effects.DealDamage(retaliation.Owner, attacker, damage, retaliation.DamageType, critical: false);
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFNeedsShieldComponent> ent, ref ActionAttemptEvent args)
    {
        if (_shields.TryGetUsableShield(args.User, out _))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-spell-needs-shield");
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<CFAuraComponent> ent, ref ComponentShutdown args)
    {
        foreach (var (index, inside) in ent.Comp.Inside)
        {
            foreach (var target in inside)
            {
                Leave(ent, ent.Comp.Effects[index], index, target);
            }
        }

        ent.Comp.Inside.Clear();
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFAuraComponent>();
        while (query.MoveNext(out var uid, out var aura))
        {
            if (now < aura.NextCheck)
                continue;

            aura.NextCheck = now + CheckEvery;
            Refresh(uid, aura);
        }
    }

    private void Refresh(EntityUid uid, CFAuraComponent aura)
    {
        var now = _timing.CurTime;
        var alive = _mobState.IsAlive(uid);
        var origin = _transform.GetMapCoordinates(uid);
        var ease = now >= aura.NextEase;

        // Both lists come from one lookup each, however many effects use them.
        List<EntityUid>? allies = null;
        List<EntityUid>? enemies = null;

        for (var index = 0; index < aura.Effects.Count; index++)
        {
            var effect = aura.Effects[index];
            if (!aura.Inside.TryGetValue(index, out var inside))
                aura.Inside[index] = inside = new HashSet<EntityUid>();

            var current = new HashSet<EntityUid>();
            if (alive && NeedsMet(uid, aura, effect))
            {
                var list = effect.Affects == CFAuraAffects.Allies
                    ? allies ??= Collect(uid, aura, origin, CFTargetFilter.Allies)
                    : enemies ??= Collect(uid, aura, origin, CFTargetFilter.Enemies);
                current.UnionWith(list);
            }

            foreach (var target in inside)
            {
                if (!current.Contains(target))
                    Leave(uid, effect, index, target);
            }

            foreach (var target in current)
            {
                if (!inside.Contains(target))
                    Enter(uid, effect, index, target);

                if (effect.KeepsFrightened && _conditions.Frightened(target) > 0)
                    _conditions.Ensure(target).FrightenedFloorUntil = now + FloorLasts;

                if (effect.EasesFrightened && ease)
                    _conditions.ReduceFrightened(target, 1);
            }

            aura.Inside[index] = current;
        }

        if (ease)
            aura.NextEase = now + EaseEvery;
    }

    private bool NeedsMet(EntityUid uid, CFAuraComponent aura, CFAuraEffect effect)
    {
        return effect.Needs switch
        {
            CFAuraNeeds.SpiritShields => _timing.CurTime < aura.SpiritShieldsUntil && _shields.TryGetRaisedShield(uid, out _),
            CFAuraNeeds.HoldingRegalia => _implements.IsHolding(uid, CFImplementKind.Regalia),
            CFAuraNeeds.HoldingLantern => _implements.IsHolding(uid, CFImplementKind.Lantern),
            CFAuraNeeds.HasBanner => _tactics.HasBanner(uid),
            _ => true,
        };
    }

    private List<EntityUid> Collect(EntityUid uid, CFAuraComponent aura, Robust.Shared.Map.MapCoordinates origin, CFTargetFilter filter)
    {
        return _effects.CollectArea(
            uid,
            CFAreaShape.Emanation,
            aura.SizeFeet,
            origin,
            origin,
            atPoint: false,
            includeCenter: true,
            selfAllowed: false,
            filter);
    }

    private void Enter(EntityUid owner, CFAuraEffect effect, int index, EntityUid target)
    {
        if (effect.Stat is { } stat)
            _bonuses.AddPermanentBonus(target, stat, effect.Type, effect.Value, effect.Trait, source: Source(owner, index));

        if (effect.RetaliateDie > 0 && effect.RetaliateType is { } type)
        {
            var retaliation = EnsureComp<CFAuraRetaliationComponent>(target);
            retaliation.Owner = owner;
            retaliation.Die = effect.RetaliateDie;
            retaliation.DamageType = type;
        }
    }

    private void Leave(EntityUid owner, CFAuraEffect effect, int index, EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return;

        if (effect.Stat != null)
            _bonuses.RemoveBonusesFrom(target, Source(owner, index));

        if (effect.RetaliateDie > 0 && CompOrNull<CFAuraRetaliationComponent>(target)?.Owner == owner)
            RemComp<CFAuraRetaliationComponent>(target);
    }

    private static string Source(EntityUid owner, int index)
    {
        return $"aura:{owner}:{index}";
    }
}
