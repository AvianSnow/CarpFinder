using System.Numerics;
using Content.Shared.CF.Detection;
using Content.Shared.Chat;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Influence;

/// <summary>
/// Paints the influence maps and reads them back. A tracker does not pathfind to its target: it follows the marks.
/// All the data is on <see cref="CFGOAPInfluenceMapComponent"/>.
/// </summary>
public sealed partial class CFGOAPInfluenceMapSystem : EntitySystem
{
    /// <summary>Four updates a second.</summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Marks fainter than this are gone.</summary>
    private const float MinimumIntensity = 0.02f;

    /// <summary>A hidden creature leaves a fainter trail. Invisible is not scentless.</summary>
    private const float SneakingStrength = 0.5f;

    private const float NoiseStrength = 1f;

    private const int NoiseRadius = 3;

    /// <summary>How much of the next step comes from where the target was heading, and how much from the trail.</summary>
    private const float HeadingBlend = 0.5f;

    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var maps = EntityQueryEnumerator<CFGOAPInfluenceMapComponent>();
        while (maps.MoveNext(out var uid, out var map))
        {
            if (now < map.NextUpdate)
                continue;

            map.NextUpdate = now + UpdateInterval;
            Decay((uid, map), (float) UpdateInterval.TotalSeconds);
        }

        var emitters = EntityQueryEnumerator<CFGOAPScentEmitterComponent, TransformComponent>();
        while (emitters.MoveNext(out var uid, out var emitter, out var xform))
        {
            if (xform.GridUid is { } grid && HasComp<CFGOAPInfluenceMapComponent>(grid))
                PaintEmitter((uid, emitter), xform, grid);
        }
    }

    /// <summary>Gives a grid a map. An advanced NPC calls this for the grid it is on.</summary>
    public void EnsureMap(EntityUid grid)
    {
        EnsureComp<CFGOAPInfluenceMapComponent>(grid);
    }

    private void PaintEmitter(Entity<CFGOAPScentEmitterComponent> ent, TransformComponent xform, EntityUid grid)
    {
        var tile = Tile(grid, xform);
        var moved = ent.Comp.LastGrid == grid && tile != ent.Comp.LastTile;
        if (ent.Comp.LastGrid == grid && !moved)
            return;

        var heading = moved ? Vector2.Normalize(tile - ent.Comp.LastTile) : Vector2.Zero;
        ent.Comp.LastGrid = grid;
        ent.Comp.LastTile = tile;

        var strength = ent.Comp.Strength;
        if (TryComp(ent, out CFStealthComponent? stealth) && stealth.Active)
            strength *= SneakingStrength;

        PaintArea(grid, ent.Comp.Layer, tile, ent.Comp.Radius, strength, heading, ent);
    }

    private void PaintArea(EntityUid grid, CFGOAPInfluenceLayer layer, Vector2i centre, int radius, float strength, Vector2 heading, EntityUid emitter)
    {
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var offset = new Vector2i(x, y);
                var falloff = 1f - (float) offset.Length / (radius + 1);
                if (falloff > 0f)
                    Paint(grid, layer, centre + offset, strength * falloff, heading, emitter);
            }
        }
    }

    /// <summary>
    /// Marks one square. Only walkable squares take a mark, so following the marks goes around walls
    /// the way the creature did.
    /// </summary>
    public void Paint(EntityUid grid, CFGOAPInfluenceLayer layer, Vector2i tile, float strength, Vector2 heading, EntityUid emitter)
    {
        if (!TryComp(grid, out CFGOAPInfluenceMapComponent? map)
            || _turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable))
            return;

        var key = (layer, ChunkOf(tile));
        if (!map.Chunks.TryGetValue(key, out var chunk))
        {
            chunk = new CFGOAPInfluenceChunk();
            map.Chunks[key] = chunk;
        }

        ref var cell = ref chunk.Cells[Index(tile)];
        cell.Intensity = MathF.Max(cell.Intensity, strength);
        cell.LastVisit = _timing.CurTime;
        cell.Heading = heading;
        cell.Emitter = emitter;
    }

    /// <summary>A one-off noise. Nearby trackers can hear it for as long as it takes to fade.</summary>
    public void MakeNoise(EntityUid source)
    {
        var xform = Transform(source);
        if (xform.GridUid is { } grid && HasComp<CFGOAPInfluenceMapComponent>(grid))
            PaintArea(grid, CFGOAPInfluenceLayer.Noise, Tile(grid, xform), NoiseRadius, NoiseStrength, Vector2.Zero, source);
    }

    /// <summary>Fades every mark, and frees blocks that have gone blank.</summary>
    public void Decay(Entity<CFGOAPInfluenceMapComponent> map, float seconds)
    {
        var scale = MathF.Pow(0.5f, seconds / MathF.Max(0.01f, map.Comp.HalfLifeSeconds));
        List<(CFGOAPInfluenceLayer, Vector2i)>? empty = null;
        foreach (var (key, chunk) in map.Comp.Chunks)
        {
            var any = false;
            for (var i = 0; i < chunk.Cells.Length; i++)
            {
                ref var cell = ref chunk.Cells[i];
                cell.Intensity *= scale;
                if (cell.Intensity < MinimumIntensity)
                    cell = default;
                else
                    any = true;
            }

            if (any)
                continue;

            empty ??= new List<(CFGOAPInfluenceLayer, Vector2i)>();
            empty.Add(key);
        }

        if (empty == null)
            return;

        foreach (var key in empty)
        {
            map.Comp.Chunks.Remove(key);
        }
    }

    /// <summary>Whether a mark no older than <paramref name="age"/> is on this creature's square.</summary>
    public bool HasRecentMark(EntityUid uid, CFGOAPInfluenceLayer layer, TimeSpan age)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid)
            return false;

        var cell = CellAt(grid, layer, xform.Coordinates);
        return cell.Intensity > 0f && _timing.CurTime - cell.LastVisit <= age;
    }

    /// <summary>The mark at a position on the grid.</summary>
    public CFGOAPInfluenceCell CellAt(EntityUid grid, CFGOAPInfluenceLayer layer, Robust.Shared.Map.EntityCoordinates coordinates)
    {
        return Cell(grid, layer, _maps.CoordinatesToTile(grid, Comp<MapGridComponent>(grid), coordinates));
    }

    public CFGOAPInfluenceCell Cell(EntityUid grid, CFGOAPInfluenceLayer layer, Vector2i tile)
    {
        return TryComp(grid, out CFGOAPInfluenceMapComponent? map) ? Cell(map, layer, tile) : default;
    }

    /// <summary>
    /// The way to go to follow a trail. It heads for the freshest mark beside it, blended with the way that mark
    /// was heading, so it never crawls the trail backward or follows an old loop.
    /// <paramref name="emitter"/> narrows it to one creature's trail.
    /// </summary>
    public bool TryFollow(EntityUid uid, CFGOAPInfluenceLayer layer, EntityUid? emitter, out Vector2 direction)
    {
        direction = Vector2.Zero;
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp(grid, out CFGOAPInfluenceMapComponent? map))
            return false;

        var tile = Tile(grid, xform);
        var here = Cell(map, layer, tile);
        var best = here;
        var bestOffset = Vector2i.Zero;
        foreach (var offset in Neighbours)
        {
            var cell = Cell(map, layer, tile + offset);
            if (Counts(cell, emitter) && IsBetter(cell, best))
            {
                best = cell;
                bestOffset = offset;
            }
        }

        if (!Counts(best, emitter))
            return false;

        var local = bestOffset == Vector2i.Zero
            ? best.Heading
            : Vector2.Normalize(bestOffset) * (1f - HeadingBlend) + best.Heading * HeadingBlend;

        if (local == Vector2.Zero)
            return false;

        direction = _transform.GetWorldRotation(grid).RotateVec(Vector2.Normalize(local));
        return true;
    }

    /// <summary>A newer mark beats a stronger one. Intensity only settles a tie.</summary>
    public static bool IsBetter(CFGOAPInfluenceCell candidate, CFGOAPInfluenceCell current)
    {
        return candidate.LastVisit != current.LastVisit
            ? candidate.LastVisit > current.LastVisit
            : candidate.Intensity > current.Intensity;
    }

    private static bool Counts(CFGOAPInfluenceCell cell, EntityUid? emitter)
    {
        return cell.Intensity > 0f && (emitter == null || cell.Emitter == emitter);
    }

    private static CFGOAPInfluenceCell Cell(CFGOAPInfluenceMapComponent map, CFGOAPInfluenceLayer layer, Vector2i tile)
    {
        return map.Chunks.TryGetValue((layer, ChunkOf(tile)), out var chunk) ? chunk.Cells[Index(tile)] : default;
    }

    private Vector2i Tile(EntityUid grid, TransformComponent xform)
    {
        return _maps.CoordinatesToTile(grid, Comp<MapGridComponent>(grid), xform.Coordinates);
    }

    private static Vector2i ChunkOf(Vector2i tile)
    {
        return new Vector2i(
            (int) MathF.Floor((float) tile.X / CFGOAPInfluenceChunk.Size),
            (int) MathF.Floor((float) tile.Y / CFGOAPInfluenceChunk.Size));
    }

    private static int Index(Vector2i tile)
    {
        var x = (tile.X % CFGOAPInfluenceChunk.Size + CFGOAPInfluenceChunk.Size) % CFGOAPInfluenceChunk.Size;
        var y = (tile.Y % CFGOAPInfluenceChunk.Size + CFGOAPInfluenceChunk.Size) % CFGOAPInfluenceChunk.Size;
        return y * CFGOAPInfluenceChunk.Size + x;
    }

    [SubscribeLocalEvent]
    private void OnMeleeAttack(Entity<CFGOAPScentEmitterComponent> ent, ref MeleeAttackEvent args)
    {
        MakeNoise(ent);
    }

    [SubscribeLocalEvent]
    private void OnShot(Entity<CFGOAPScentEmitterComponent> ent, ref SelfBeforeGunShotEvent args)
    {
        MakeNoise(ent);
    }

    [SubscribeLocalEvent]
    private void OnSpoke(Entity<CFGOAPScentEmitterComponent> ent, ref EntitySpokeEvent args)
    {
        MakeNoise(ent);
    }
}
