using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Items;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.CF.Traits;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
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

    /// <summary>Courageous Anthem is a 60-foot emanation.</summary>
    public const int AnthemFeet = 60;

    private static readonly ProtoId<CFTraitPrototype> Fear = "Fear";

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
    [Dependency] private CFIkonSystem _ikons = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = new();

    /// <param name="sizeFeet">A larger emanation than the aura has. 0 leaves it alone.</param>
    public void Add(EntityUid uid, IEnumerable<CFAuraEffect> effects, int sizeFeet = 0)
    {
        var aura = EnsureComp<CFAuraComponent>(uid);
        aura.Effects.AddRange(effects);
        aura.SizeFeet = Math.Max(aura.SizeFeet, sizeFeet);
        Dirty(uid, aura);
    }

    /// <summary>True if a living creature is inside this creature's aura right now.</summary>
    public bool Contains(EntityUid owner, EntityUid other)
    {
        return TryComp(owner, out CFAuraComponent? aura)
               && Collect(owner, aura.SizeFeet, _transform.GetMapCoordinates(owner), CFTargetFilter.All).Contains(other);
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

    /// <summary>
    /// Starts Courageous Anthem, or keeps it going, for <paramref name="lasts"/> from now. It never shortens what is left.
    /// The first time, the creature gets the anthem's effects: +1 status to attack and damage rolls and to saves against
    /// fear, for itself and allies in a 60-foot emanation.
    /// </summary>
    public void Anthem(EntityUid uid, TimeSpan lasts)
    {
        var aura = EnsureComp<CFAuraComponent>(uid);
        if (!HasAnthem(aura))
            aura.Effects.AddRange(AnthemEffects());

        var now = _timing.CurTime;
        var running = now < aura.AnthemUntil;
        if (now + lasts > aura.AnthemUntil)
            aura.AnthemUntil = now + lasts;

        // A running anthem only needs its end pushed on: the usual checks keep who is inside up to date.
        if (!running)
            Refresh(uid, aura);
    }

    /// <summary>True while Courageous Anthem is running on this creature.</summary>
    public bool AnthemActive(EntityUid uid)
    {
        return TryComp(uid, out CFAuraComponent? aura) && _timing.CurTime < aura.AnthemUntil;
    }

    private static bool HasAnthem(CFAuraComponent aura)
    {
        foreach (var effect in aura.Effects)
        {
            if (effect.Needs == CFAuraNeeds.Anthem)
                return true;
        }

        return false;
    }

    private static CFAuraEffect[] AnthemEffects()
    {
        return new[]
        {
            AnthemEffect(CFBonusStat.AttackRoll, null),
            AnthemEffect(CFBonusStat.Damage, null),
            AnthemEffect(CFBonusStat.Saves, Fear),
        };
    }

    private static CFAuraEffect AnthemEffect(CFBonusStat stat, ProtoId<CFTraitPrototype>? trait)
    {
        return new CFAuraEffect
        {
            Affects = CFAuraAffects.Allies,
            Needs = CFAuraNeeds.Anthem,
            SizeFeet = AnthemFeet,
            IncludeSelf = true,
            Stat = stat,
            Type = CFBonusType.Status,
            Value = 1,
            Trait = trait,
        };
    }

    /// <summary>Call when <paramref name="attacker"/> makes an attack against <paramref name="target"/>, hit or miss.</summary>
    public void NoteAttacked(EntityUid target, EntityUid attacker)
    {
        if (!_net.IsServer || !TryComp(target, out CFAuraRetaliationComponent? retaliation) || !_mobState.IsAlive(attacker))
            return;

        // One extra die for every 2 ranks above the first, by the owner's focus spell rank.
        var level = CompOrNull<CFCombatantComponent>(retaliation.AuraOwner)?.Level ?? 1;
        var dice = 1 + (CFCasting.CantripRank(level) - 1) / 2;
        var damage = 0;
        for (var i = 0; i < dice; i++)
        {
            damage += _random.Next(1, retaliation.Die + 1);
        }

        _effects.DealDamage(retaliation.AuraOwner, attacker, damage, retaliation.DamageType, critical: false);
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

        // One lookup for each size and side, however many effects use it.
        var lists = new Dictionary<(int Size, CFAuraAffects Affects), List<EntityUid>>();
        var sizes = new List<int>();

        for (var index = 0; index < aura.Effects.Count; index++)
        {
            var effect = aura.Effects[index];
            if (!aura.Inside.TryGetValue(index, out var inside))
                aura.Inside[index] = inside = new HashSet<EntityUid>();

            var current = new HashSet<EntityUid>();
            if (alive && NeedsMet(uid, aura, effect))
            {
                var size = effect.SizeFeet > 0 ? effect.SizeFeet : aura.SizeFeet;
                if (!sizes.Contains(size))
                    sizes.Add(size);

                var key = (size, effect.Affects);
                if (!lists.TryGetValue(key, out var list))
                {
                    var filter = effect.Affects == CFAuraAffects.Allies ? CFTargetFilter.Allies : CFTargetFilter.Enemies;
                    lists[key] = list = Collect(uid, size, origin, filter);
                }

                current.UnionWith(list);
                if (effect.IncludeSelf && effect.Affects == CFAuraAffects.Allies)
                    current.Add(uid);
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

        sizes.Sort();
        var active = sizes.Count > 0;
        if (aura.Active != active || !SameSizes(aura.ActiveSizes, sizes))
        {
            aura.Active = active;
            aura.ActiveSizes = sizes;
            Dirty(uid, aura);
        }
    }

    private static bool SameSizes(List<int> a, List<int> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private bool NeedsMet(EntityUid uid, CFAuraComponent aura, CFAuraEffect effect)
    {
        return effect.Needs switch
        {
            CFAuraNeeds.SpiritShields => _timing.CurTime < aura.SpiritShieldsUntil && _shields.TryGetRaisedShield(uid, out _),
            CFAuraNeeds.HoldingRegalia => _implements.IsHolding(uid, CFImplementKind.Regalia),
            CFAuraNeeds.HoldingLantern => _implements.IsHolding(uid, CFImplementKind.Lantern),
            CFAuraNeeds.HasBanner => _tactics.HasBanner(uid),
            CFAuraNeeds.Immanence => _ikons.IsEmpowered(uid, effect.Ikon),
            CFAuraNeeds.Anthem => _timing.CurTime < aura.AnthemUntil,
            _ => true,
        };
    }

    /// <summary>
    /// How far an emanation of this size reaches from its owner's centre, in tiles: the size, plus the half of the owner's
    /// own square. The client draws its border at the same distance, so the line shows exactly who is inside.
    /// </summary>
    public static float Radius(int sizeFeet)
    {
        return Math.Max(1f, sizeFeet / CFAreaGrid.FeetPerTile) + 0.5f;
    }

    /// <summary>
    /// The living creatures whose centre is inside the circle. A circle around where the owner really stands, not the
    /// tiles, so the aura moves smoothly with its owner.
    /// </summary>
    private List<EntityUid> Collect(EntityUid uid, int sizeFeet, MapCoordinates origin, CFTargetFilter filter)
    {
        var found = new List<EntityUid>();
        var radius = Radius(sizeFeet);
        _nearby.Clear();
        _lookup.GetEntitiesInRange(origin, radius, _nearby);
        foreach (var (other, _) in _nearby)
        {
            var position = _transform.GetMapCoordinates(other);
            if (position.MapId != origin.MapId || (position.Position - origin.Position).Length() > radius)
                continue;

            if (_effects.PassesFilter(uid, other, filter, selfAllowed: false))
                found.Add(other);
        }

        return found;
    }

    private void Enter(EntityUid owner, CFAuraEffect effect, int index, EntityUid target)
    {
        if (effect.Stat is { } stat)
            _bonuses.AddPermanentBonus(target, stat, effect.Type, effect.Value, effect.Trait, source: Source(owner, index));

        if (effect.RetaliateDie > 0 && effect.RetaliateType is { } type)
        {
            var retaliation = EnsureComp<CFAuraRetaliationComponent>(target);
            retaliation.AuraOwner = owner;
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

        if (effect.RetaliateDie > 0 && CompOrNull<CFAuraRetaliationComponent>(target)?.AuraOwner == owner)
            RemComp<CFAuraRetaliationComponent>(target);
    }

    /// <summary>Every bonus an aura gives has a source starting with this.</summary>
    public const string SourcePrefix = "aura:";

    /// <summary>True for a bonus given by an aura, which the HUD shows with the spells' bonuses.</summary>
    public static bool IsAuraSource(string? source)
    {
        return source != null && source.StartsWith(SourcePrefix, StringComparison.Ordinal);
    }

    private static string Source(EntityUid owner, int index)
    {
        return $"{SourcePrefix}{owner}:{index}";
    }
}
