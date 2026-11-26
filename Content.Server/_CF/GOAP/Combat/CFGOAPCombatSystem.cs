using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Combat;

/// <summary>
/// Swings and shoots for GOAP creatures. A step that wants a creature hit puts <see cref="CFGOAPMeleeComponent"/> on
/// the attacker, and each tick this swings the held weapon, or the body's own attack, at the target once it is in reach
/// with a clear line; <c>CFStrikeSystem</c> turns the swing into a Strike. <see cref="CFGOAPRangedComponent"/> does the
/// same with a gun or bow in hand, after facing the target and a moment's aim. Stock NPC combat played this part until
/// 26 November 2026, and walked the creature after its target as it did; here where a creature stands is the steps'
/// business alone. <c>npc.enabled</c> stops this too.
/// </summary>
public sealed partial class CFGOAPCombatSystem : EntitySystem
{
    /// <summary>Further than this from its target, in tiles, a melee attacker does not try.</summary>
    private const float MeleeLostRange = 14f;

    /// <summary>How long a target has to stay in a clear line before the first shot, in seconds.</summary>
    private const float ShootDelay = 0.2f;

    /// <summary>How often the line to the target is checked, in seconds.</summary>
    private const float LineCheckInterval = 0.2f;

    /// <summary>How fast a shot is taken to fly, in tiles a second, for aiming ahead of a moving target.</summary>
    private const float ShotSpeed = 20f;

    /// <summary>How far off the target it may still face when it shoots.</summary>
    private static readonly Angle Accuracy = Angle.FromDegrees(30);

    /// <summary>What blocks a shot: walls and anything a hand cannot reach past, as stock NPC shooting had it.</summary>
    private const CollisionGroup LineMask = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;

    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The cvar, cached. Settings are not game state.</summary>
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, CCVars.NPCEnabled, value => _enabled = value, true);
    }

    [SubscribeLocalEvent]
    private void OnMeleeStartup(Entity<CFGOAPMeleeComponent> ent, ref ComponentStartup args)
    {
        SetCombatMode(ent, true);
    }

    [SubscribeLocalEvent]
    private void OnMeleeShutdown(Entity<CFGOAPMeleeComponent> ent, ref ComponentShutdown args)
    {
        SetCombatMode(ent, false);
    }

    [SubscribeLocalEvent]
    private void OnRangedStartup(Entity<CFGOAPRangedComponent> ent, ref ComponentStartup args)
    {
        SetCombatMode(ent, true);
    }

    [SubscribeLocalEvent]
    private void OnRangedShutdown(Entity<CFGOAPRangedComponent> ent, ref ComponentShutdown args)
    {
        SetCombatMode(ent, false);
    }

    /// <summary>In combat mode while it fights, and out of it after, as stock NPC combat did.</summary>
    private void SetCombatMode(EntityUid uid, bool value)
    {
        if (!TerminatingOrDeleted(uid) && TryComp(uid, out CombatModeComponent? mode))
            _combatMode.SetInCombatMode(uid, value, mode);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled)
            return;

        var now = _timing.CurTime;
        UpdateMelee(now);
        UpdateRanged(frameTime, now);
    }

    private void UpdateMelee(TimeSpan now)
    {
        var query = EntityQueryEnumerator<CFGOAPMeleeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var melee, out var xform))
        {
            // Something else took it out of combat mode, so it is done fighting.
            if (!TryComp(uid, out CombatModeComponent? mode) || !mode.IsInCombatMode)
            {
                RemComp<CFGOAPMeleeComponent>(uid);
                continue;
            }

            if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon)
                || weapon.NextAttack > now
                || !TryComp(melee.Target, out TransformComponent? targetXform)
                || !xform.Coordinates.TryDistance(EntityManager, targetXform.Coordinates, out var distance)
                || distance > MeleeLostRange
                || distance > weapon.Range
                || !_interaction.InRangeUnobstructed(uid, melee.Target, distance + 0.1f))
                continue;

            _melee.AttemptLightAttack(uid, weaponUid, weapon, melee.Target);
        }
    }

    private void UpdateRanged(float frameTime, TimeSpan now)
    {
        var query = EntityQueryEnumerator<CFGOAPRangedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var ranged, out var xform))
        {
            if (!TryComp(ranged.Target, out TransformComponent? targetXform)
                || !TryComp(ranged.Target, out PhysicsComponent? targetBody)
                || targetXform.MapID != xform.MapID
                || !_gun.TryGetGun(uid, out var gun))
            {
                ranged.ShootAccumulator = 0f;
                continue;
            }

            // Out of arrows it waits here; the shot sensor sees that and the brain plans something else.
            var ammo = new GetAmmoCountEvent();
            RaiseLocalEvent(gun, ref ammo);
            if (ammo.Count == 0)
            {
                ranged.ShootAccumulator = 0f;
                continue;
            }

            var here = _transform.GetWorldPosition(xform);
            var there = _transform.GetWorldPosition(targetXform);
            var distance = (there - here).Length();
            ranged.LineAccumulator -= frameTime;
            if (ranged.LineAccumulator < 0f)
            {
                ranged.LineAccumulator += LineCheckInterval;
                ranged.TargetInLine = _interaction.InRangeUnobstructed(uid, ranged.Target, distance + 0.1f, LineMask);
            }

            if (!ranged.TargetInLine)
            {
                ranged.ShootAccumulator = 0f;
                continue;
            }

            ranged.ShootAccumulator += frameTime;
            if (ranged.ShootAccumulator < ShootDelay)
                continue;

            // Aimed at where the target will be when the shot gets there.
            var spot = there + targetBody.LinearVelocity * distance / ShotSpeed;
            if (!_rotate.TryRotateTo(uid, (spot - here).ToWorldAngle(), frameTime, Accuracy, xform: xform)
                || !_gun.CanShoot(gun)
                || gun.Comp.NextFire > now)
                continue;

            var coordinates = _maps.TryFindGridAt(xform.MapID, there, out var grid, out var gridComp)
                ? new EntityCoordinates(grid, _maps.WorldToLocal(grid, gridComp, spot))
                : new EntityCoordinates(xform.MapUid!.Value, spot);
            _gun.AttemptShoot(uid, gun, coordinates, ranged.Target);
        }
    }
}
