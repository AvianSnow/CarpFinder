using System.Numerics;
using Content.Shared.CF.Factions;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Hazards;

/// <summary>
/// Builds a hazard on the tile next to a creature. The hazard takes the builder's faction, and
/// <see cref="CFHazardSystem.IsVictim"/> does the rest: its allies never set it off, so no AI has to walk around it.
/// </summary>
public sealed partial class CFTrapLayerSystem : EntitySystem
{
    /// <summary>Under a tile, so this only rules out the tile itself.</summary>
    private const float SameTile = 0.5f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private CFFactionSystem _factions = default!;

    /// <summary>The tile next to the creature in this direction, if a trap can go there.</summary>
    /// <param name="spacing">No armed hazard may be this near, in tiles.</param>
    public bool TryFindSpot(EntityUid user, Vector2 direction, float spacing, out EntityCoordinates spot)
    {
        spot = default;
        if (direction == Vector2.Zero
            || Transform(user).GridUid is not { } grid
            || !TryComp(grid, out MapGridComponent? gridComp))
            return false;

        // A grid can be turned, as an airship is, and its tiles turn with it.
        var local = (-_transform.GetWorldRotation(grid)).RotateVec(direction);
        var tile = _maps.CoordinatesToTile(grid, gridComp, Transform(user).Coordinates) + local.ToWorldAngle().GetDir().ToIntVec();

        if (!_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef)
            || tileRef.Tile.IsEmpty
            || _turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable))
            return false;

        spot = _maps.GridTileToLocal(grid, gridComp, tile);
        foreach (var hazard in _lookup.GetEntitiesInRange<CFHazardComponent>(spot, spacing))
        {
            if (hazard.Comp.Armed)
                return false;
        }

        return true;
    }

    /// <summary>Whether a player could build one now, on the tile they are facing.</summary>
    public bool CanConstruct(EntityUid user)
    {
        return TryComp(user, out CFTrapLayerComponent? layer)
               && layer.Charges > 0
               && TryFindSpot(user, _transform.GetWorldRotation(user).ToWorldVec(), SameTile, out _);
    }

    /// <summary>A player's Construct Trap. The action has already paid for it.</summary>
    public void TryConstruct(EntityUid user)
    {
        if (TryComp(user, out CFTrapLayerComponent? layer)
            && TryFindSpot(user, _transform.GetWorldRotation(user).ToWorldVec(), SameTile, out var spot))
            TryLay((user, layer), spot);
    }

    /// <summary>Spends one charge on a trap at this spot. It belongs to whichever side the builder is on.</summary>
    public bool TryLay(Entity<CFTrapLayerComponent> ent, EntityCoordinates spot)
    {
        if (!_net.IsServer || ent.Comp.Charges <= 0)
            return false;

        var trap = SpawnAtPosition(ent.Comp.Trap, spot);
        if (TryComp(ent, out CFFactionComponent? faction))
            _factions.SetFaction(trap, faction.Faction);

        ent.Comp.Charges--;
        ent.Comp.NextLay = _timing.CurTime + ent.Comp.Cooldown;
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("cf-trap-laid", ("left", ent.Comp.Charges)), ent, ent);
        return true;
    }
}
