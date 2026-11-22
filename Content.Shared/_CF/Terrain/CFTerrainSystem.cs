using Content.Shared.CF.Actions;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Terrain;

/// <summary>
/// Difficult terrain. A creature whose centre is on a tile of difficult terrain moves at half its Speed, or a third on
/// greater difficult terrain: the real-time form of each square costing 5 or 10 extra feet. A tile counts when the tile
/// itself has <c>cfDifficultTerrain</c>, or something on it has <see cref="CFDifficultTerrainComponent"/>, or the creature
/// is inside an enemy's Hampering Stance. Ignoring a kind of difficult terrain makes greater difficult terrain of that
/// kind only difficult, as the rules say. Jumping, flying and being thrown cross it freely.
/// </summary>
/// <remarks>
/// The server looks a few times a second at every creature that is moving, and sends the result in
/// <see cref="CFTerrainWalkerComponent.Multiplier"/>. Both sides read that number for the Speed.
/// </remarks>
public sealed partial class CFTerrainSystem : EntitySystem
{
    private const float CheckEvery = 0.2f;
    private const float Difficult = 0.5f;
    private const float Greater = 1f / 3f;

    private const LookupFlags Flags = LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFStanceSystem _stances = default!;

    private readonly HashSet<Entity<CFDifficultTerrainComponent>> _found = new();
    private float _accumulator;

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        _accumulator += frameTime;
        if (_accumulator < CheckEvery)
            return;

        _accumulator = 0f;
        var query = EntityQueryEnumerator<MovementSpeedModifierComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var physics, out var xform))
        {
            if (!physics.Awake && !HasComp<CFTerrainWalkerComponent>(uid))
                continue;

            Check(uid, physics, xform);
        }
    }

    /// <summary>Looks again at once at what <paramref name="uid"/> stands in.</summary>
    public void Recheck(EntityUid uid)
    {
        if (_net.IsServer && TryComp(uid, out PhysicsComponent? physics) && HasComp<MovementSpeedModifierComponent>(uid))
            Check(uid, physics, Transform(uid));
    }

    /// <summary>True while the creature is in difficult terrain it does not ignore. It cannot Step there without Feather Step.</summary>
    public bool InDifficultTerrain(EntityUid uid)
    {
        return CompOrNull<CFTerrainWalkerComponent>(uid)?.InTerrain == true;
    }

    /// <summary>False when the creature would be stepping in difficult terrain, which the rules forbid.</summary>
    public bool CanStep(EntityUid uid)
    {
        return !TryComp(uid, out CFTerrainWalkerComponent? walker) || !walker.InTerrain || walker.FeatherStep;
    }

    public CFTerrainWalkerComponent Walker(EntityUid uid)
    {
        return EnsureComp<CFTerrainWalkerComponent>(uid);
    }

    /// <summary>The creature ignores every difficult terrain for this long (Light Paws).</summary>
    public void IgnoreAllFor(EntityUid uid, float seconds)
    {
        var walker = Walker(uid);
        var until = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        if (until > walker.IgnoreAllUntil)
            walker.IgnoreAllUntil = until;

        Dirty(uid, walker);
        Recheck(uid);
    }

    /// <summary>The creature is inside <paramref name="owner"/>'s Hampering Stance, or has left it.</summary>
    public void SetHampered(EntityUid uid, EntityUid owner, bool inside)
    {
        if (!inside && !HasComp<CFTerrainWalkerComponent>(uid))
            return;

        var walker = Walker(uid);
        if (inside ? walker.HamperedBy.Add(owner) : walker.HamperedBy.Remove(owner))
            Recheck(uid);
    }

    private void Check(EntityUid uid, PhysicsComponent physics, TransformComponent xform)
    {
        TryComp(uid, out CFTerrainWalkerComponent? walker);
        var level = Airborne(uid, physics) ? 0 : Level(uid, walker, xform);
        if (level == 0 && walker == null)
            return;

        walker ??= EnsureComp<CFTerrainWalkerComponent>(uid);
        var now = _timing.CurTime;
        var inTerrain = level > 0;
        if (inTerrain && !walker.InTerrain)
            walker.EnteredAt = now;

        var multiplier = level switch
        {
            0 => 1f,
            1 => Difficult,
            _ => Greater,
        };

        // Dragon Stance: the first square of difficult terrain is free. Here that is the time it takes to cross one.
        if (inTerrain && _stances.Current(uid)?.FreeTerrainFeet is > 0 and var feet && TryComp(uid, out MovementSpeedModifierComponent? movement))
        {
            var tilesPerSecond = Math.Max(0.1f, movement.BaseSprintSpeed);
            if (now - walker.EnteredAt < TimeSpan.FromSeconds(feet / 5f / tilesPerSecond))
                multiplier = 1f;
        }

        if (walker.InTerrain == inTerrain && MathHelper.CloseTo(walker.Multiplier, multiplier))
            return;

        walker.InTerrain = inTerrain;
        walker.Multiplier = multiplier;
        Dirty(uid, walker);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private bool Airborne(EntityUid uid, PhysicsComponent physics)
    {
        return physics.BodyStatus == BodyStatus.InAir || HasComp<ThrownItemComponent>(uid);
    }

    /// <summary>0 for easy ground, 1 for difficult terrain and 2 for greater, after what the creature ignores.</summary>
    private int Level(EntityUid uid, CFTerrainWalkerComponent? walker, TransformComponent xform)
    {
        if (walker != null && _timing.CurTime < walker.IgnoreAllUntil)
            return 0;

        var level = 0;
        if (walker != null && walker.HamperedBy.Count > 0)
            level = Math.Max(level, Count(walker, CFTerrainCause.Creature, greater: false, magical: false));

        if (xform.GridUid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return level;

        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        if (_map.TryGetTileRef(gridUid, grid, tile, out var tileRef)
            && _tiles[tileRef.Tile.TypeId] is ContentTileDefinition { CFDifficultTerrain: { } cause } definition)
        {
            level = Math.Max(level, Count(walker, cause, definition.CFGreaterDifficultTerrain, magical: false));
        }

        _found.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, 1f, _found, Flags);
        foreach (var (terrainUid, terrain) in _found)
        {
            if (terrainUid == uid)
                continue;

            var other = Transform(terrainUid);
            if (other.GridUid != gridUid || _map.TileIndicesFor(gridUid, grid, other.Coordinates) != tile)
                continue;

            level = Math.Max(level, Count(walker, terrain.Cause, terrain.Greater, terrain.Magical));
            if (level == 2)
                break;
        }

        return level;
    }

    private static int Count(CFTerrainWalkerComponent? walker, CFTerrainCause cause, bool greater, bool magical)
    {
        var level = greater ? 2 : 1;
        if (walker != null && (walker.Ignores.Contains(cause) || !magical && walker.IgnoresNonMagical.Contains(cause)))
            level--;

        return level;
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<CFTerrainWalkerComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Multiplier < 1f)
            args.ModifySpeed(ent.Comp.Multiplier, ent.Comp.Multiplier);
    }

    [SubscribeLocalEvent]
    private void OnState(Entity<CFTerrainWalkerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }
}
