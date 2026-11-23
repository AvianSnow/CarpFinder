using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server.CF.Companions;

/// <summary>
/// Makes thralls, sends one at a creature for a round at a time, and sends them away when their minute is up. Between
/// orders they fight by themselves, on their GOAP brains, like any companion.
/// </summary>
public sealed partial class CFThrallServerSystem : EntitySystem
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OrderLasts = TimeSpan.FromSeconds(6);

    private static readonly Vector2[] Offsets =
    {
        new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f), new(1f, 1f), new(-1f, 1f), new(1f, -1f), new(-1f, -1f),
    };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFCompanionSystem _companions = default!;
    [Dependency] private CFSummonedSystem _summoned = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private Content.Shared.CF.Factions.CFFactionSystem _factions = default!;

    /// <summary>Adjacent, for Thrall Teamwork: within a tile and a half, centre to centre.</summary>
    private const float AdjacentTiles = 1.6f;

    [SubscribeLocalEvent]
    private void OnCreate(Entity<CFNecromancerComponent> ent, ref CFCreateThrallsEvent args)
    {
        // Inevitable Return raises one on the corpse, not beside the caster, and the count stands as it is.
        var count = args.At == null ? args.Count + ent.Comp.ExtraThralls : args.Count;
        var origin = args.At ?? Transform(ent).Coordinates;
        var level = CompOrNull<CFCombatantComponent>(ent)?.Level ?? 1;
        for (var i = 0; i < count; i++)
        {
            var thrall = Spawn("CFThrall", args.At == null ? origin.Offset(Offsets[i % Offsets.Length]) : origin);
            _companions.Bind(ent, thrall);

            var comp = EnsureComp<CFThrallComponent>(thrall);
            comp.Necromancer = ent;
            comp.Expires = _timing.CurTime + Lifetime;

            if (TryComp(thrall, out CFCombatOptionsComponent? options))
            {
                // 1d6 at level 1, and 1d6 more at level 5 and every 4 levels after.
                options.NaturalDice = 1 + (level - 1) / 4;
                if (!string.IsNullOrEmpty(ent.Comp.DamageType))
                    options.NaturalType = ent.Comp.DamageType;

                // Mobile Thralls: a climb Speed for as long as it lasts.
                if (ent.Comp.Climb)
                    options.ClimbSpeedUntil = TimeSpan.MaxValue;

                Dirty(thrall, options);
            }

            if (ent.Comp.Trait.Length > 0 && TryComp(thrall, out Content.Shared.CF.Traits.CFTraitComponent? traits))
            {
                traits.Traits.Add(ent.Comp.Trait);
                Dirty(thrall, traits);
            }

            if (ent.Comp.ExtraSpeed != 0)
                _bonuses.AddPermanentBonus(thrall, CFBonusStat.Speed, CFBonusType.Status, ent.Comp.ExtraSpeed);
        }

        if (args.Teamwork && ent.Comp.Teamwork)
            Teamwork(ent);
    }

    /// <summary>Thrall Teamwork: a free melee Strike at an enemy in the Reaper's reach that stands beside one of its thralls.</summary>
    private void Teamwork(EntityUid reaper)
    {
        var position = _transform.GetWorldPosition(reaper);
        var mapId = Transform(reaper).MapID;
        var query = EntityQueryEnumerator<CFCombatantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var combat, out var xform))
        {
            if (uid == reaper || xform.MapID != mapId || combat.CurrentHp <= 0 || !_mobState.IsAlive(uid) || !_factions.CanHarm(reaper, uid))
                continue;

            var at = _transform.GetWorldPosition(xform);
            if ((at - position).Length() > AdjacentTiles || !BesideThrall(reaper, at))
                continue;

            if (_strikes.TryMeleeWeapon(reaper, out var stats, out var weapon))
                _strikes.PerformStrike(reaper, uid, stats, weapon, melee: true);

            return;
        }
    }

    private bool BesideThrall(EntityUid owner, Vector2 position)
    {
        var mapId = Transform(owner).MapID;
        var query = EntityQueryEnumerator<CFThrallComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thrall, out var xform))
        {
            if (thrall.Necromancer == owner && xform.MapID == mapId && _mobState.IsAlive(uid) && (_transform.GetWorldPosition(xform) - position).Length() <= AdjacentTiles)
                return true;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnCharge(Entity<CFNecromancerComponent> ent, ref CFChargeThrallEvent args)
    {
        Command(ent, args.Target, args.Dice);
    }

    [SubscribeLocalEvent]
    private void OnCommand(Entity<CFCommandThrallActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        if (!Command(args.Performer, args.Target, 0))
        {
            _popup.PopupEntity(Loc.GetString("cf-thrall-none"), args.Performer, args.Performer);
            return;
        }

        args.Handled = true;
    }

    /// <summary>Orders the owner's thrall nearest to the target to attack it, for one round. False if the owner has none.</summary>
    public bool Command(EntityUid owner, EntityUid target, int chargeDice)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var position = _transform.GetWorldPosition(target);
        var query = EntityQueryEnumerator<CFThrallComponent>();
        while (query.MoveNext(out var uid, out var thrall))
        {
            if (thrall.Necromancer != owner || !_mobState.IsAlive(uid))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - position).LengthSquared();
            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        if (best is not { } chosen)
            return false;

        var comp = Comp<CFThrallComponent>(chosen);
        comp.OrderUntil = _timing.CurTime + OrderLasts;
        comp.ChargeDice = chargeDice;
        _companions.OrderAttack(chosen, target);
        return true;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFThrallComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thrall, out var thrallXform))
        {
            // A dead thrall has already gone in a puff of smoke (CFSummonedSystem).
            if (now >= thrall.Expires)
            {
                _summoned.Unsummon(uid);
                continue;
            }

            if (thrall.OrderUntil != TimeSpan.Zero && now >= thrall.OrderUntil)
            {
                thrall.OrderUntil = TimeSpan.Zero;
                thrall.ChargeDice = 0;
                _companions.StopOrder(uid);
            }

            // Song of the Soul: fast healing for the one creature that hears it, while it stays close.
            if (thrall.SongFor is { } listener && now >= thrall.NextSong)
            {
                thrall.NextSong = now + OrderLasts;
                if (TerminatingOrDeleted(listener) || !_mobState.IsAlive(listener))
                    thrall.SongFor = null;
                else if (Transform(listener).MapID == thrallXform.MapID
                         && (_transform.GetWorldPosition(listener) - _transform.GetWorldPosition(thrallXform)).Length() <= 15f / CFMelee.FeetPerTile + 0.5f)
                    _effects.Heal(uid, listener, thrall.SongHealing);
            }
        }
    }
}
