using System.Numerics;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Lighting;

/// <summary>How bright a place is, as the rules count it.</summary>
public enum CFLightLevel : byte
{
    Darkness,
    Dim,
    Bright,
}

/// <summary>What the light does to how well one creature sees another.</summary>
public enum CFObscured : byte
{
    None,

    /// <summary>Dim light, to eyes with no low-light vision: a DC 5 flat check to target it.</summary>
    Concealed,

    /// <summary>Darkness, to eyes with no darkvision: a DC 11 flat check, and it attacks as an unseen creature.</summary>
    Hidden,
}

/// <summary>
/// Light and darkness. Nothing runs each tick: a place is worked out when something asks, from the map's ambient
/// light or its sun, roofs, and the point lights with a clear line to it, and kept for half a second.
/// The namespace is Lighting because <c>Content.Shared.Light</c> is stock's.
/// </summary>
public sealed partial class CFLightSystem : EntitySystem
{
    /// <summary>Lights farther than this are not looked for. Stock's largest radius is about this.</summary>
    private const float MaxLightTiles = 10f;

    /// <summary>The share of a light's radius that is bright. The rest is dim, as a torch's 20 feet and 20 more.</summary>
    private const float BrightShare = 0.5f;

    /// <summary>The sun's level, from 0 at midnight, above which it is day. Below it the night sky gives dim light.</summary>
    private const double DayLevel = 0.3;

    private const float BrightAmbient = 0.3f;
    private const float DimAmbient = 0.08f;

    /// <summary>A light weaker than this is a glow: dim light all the way in. Give a dim-only light <see cref="DimOnlyEnergy"/>.</summary>
    private const float BrightEnergy = 0.5f;

    public const float DimOnlyEnergy = 0.4f;

    private const int CacheLimit = 4096;

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(0.5);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityQuery<OccluderComponent> _occluders = default!;

    // A cache, not game state: it is rebuilt from the world whenever it is asked.
    private readonly Dictionary<(MapId, Vector2i), (CFLightLevel Level, TimeSpan At)> _cache = new();
    private readonly HashSet<EntityUid> _found = new();

    /// <summary>Whether light counts at all. Off, everywhere is bright.</summary>
    public bool Enabled => _cfg.GetCVar(CFCVars.LightRules);

    /// <summary>How bright it is where this creature stands.</summary>
    public CFLightLevel LevelAt(EntityUid uid)
    {
        return LevelAt(_transform.GetMapCoordinates(uid));
    }

    public CFLightLevel LevelAt(MapCoordinates coords)
    {
        if (!Enabled || coords.MapId == MapId.Nullspace)
            return CFLightLevel.Bright;

        var key = (coords.MapId, new Vector2i((int) MathF.Floor(coords.X), (int) MathF.Floor(coords.Y)));
        var now = _timing.CurTime;
        if (_cache.TryGetValue(key, out var kept) && now >= kept.At && now - kept.At < CacheTime)
            return kept.Level;

        var level = Compute(coords);
        if (_cache.Count >= CacheLimit)
            _cache.Clear();

        _cache[key] = (level, now);
        return level;
    }

    /// <summary>Forgets what was worked out. For tests, and for anything that changes the light and asks at once.</summary>
    public void Forget()
    {
        _cache.Clear();
    }

    /// <summary>What the light where <paramref name="target"/> stands does to <paramref name="observer"/>'s view of it.</summary>
    public CFObscured Obscured(EntityUid target, EntityUid observer)
    {
        // Only eyes are fooled by the dark: a trap or an object rolls no flat check.
        if (target == observer || !Enabled || !HasComp<MobStateComponent>(observer) || SeesInDarkness(observer))
            return CFObscured.None;

        switch (LevelAt(target))
        {
            case CFLightLevel.Bright:
                return CFObscured.None;
            case CFLightLevel.Dim:
                return CompOrNull<CFSensesComponent>(observer)?.LowLightVision == true ? CFObscured.None : CFObscured.Concealed;
            default:
                return CFObscured.Hidden;
        }
    }

    /// <summary>Darkvision, from birth, an elixir or spell, or a Rage.</summary>
    public bool SeesInDarkness(EntityUid uid)
    {
        if (CompOrNull<CFSensesComponent>(uid)?.Darkvision == true)
            return true;

        if (CompOrNull<CFConditionComponent>(uid)?.TimedDarkvision == true)
            return true;

        return CompOrNull<CFCombatOptionsComponent>(uid) is { Raging: true, RageDarkvision: true };
    }

    private CFLightLevel Compute(MapCoordinates coords)
    {
        var level = Ambient(coords);
        if (level == CFLightLevel.Bright)
            return level;

        _found.Clear();
        _lookup.GetEntitiesInRange(coords.MapId, coords.Position, MaxLightTiles, _found, LookupFlags.Uncontained | LookupFlags.Contained);
        foreach (var uid in _found)
        {
            if (!_lights.TryGetLight(uid, out var light)
                || !light.Enabled
                || light.ContainerOccluded
                || light.Energy <= 0f
                || light.Radius <= 0f)
            {
                continue;
            }

            var from = _transform.GetWorldPosition(uid);
            var distance = (coords.Position - from).Length();
            if (distance > light.Radius)
                continue;

            var reached = light.Energy >= BrightEnergy && distance <= light.Radius * BrightShare ? CFLightLevel.Bright : CFLightLevel.Dim;
            if (reached <= level || Blocked(coords.MapId, from, coords.Position, distance))
                continue;

            level = reached;
            if (level == CFLightLevel.Bright)
                break;
        }

        return level;
    }

    /// <summary>The sky's light: none under a roof, otherwise the sun by time of day or the map's fixed ambient light.</summary>
    private CFLightLevel Ambient(MapCoordinates coords)
    {
        if (!_maps.TryGetMap(coords.MapId, out var mapUid) || !TryComp(mapUid, out MapLightComponent? ambient))
            return CFLightLevel.Darkness;

        if (_maps.TryFindGridAt(coords, out var gridUid, out var grid)
            && TryComp(gridUid, out RoofComponent? roof)
            && _roof.IsRooved((gridUid, grid, roof), _maps.TileIndicesFor(gridUid, grid, coords)))
        {
            return CFLightLevel.Darkness;
        }

        if (TryComp(mapUid, out LightCycleComponent? cycle) && cycle.Enabled)
        {
            // The same clock the client draws the sky by.
            var time = (float) _timing.CurTime
                .Add(cycle.Offset)
                .Subtract(_ticker.RoundStartTimeSpan)
                .Subtract(_metadata.GetPauseTime(mapUid.Value))
                .TotalSeconds;

            // Night outdoors is moonlight: dim, never darkness.
            return SharedLightCycleSystem.CalculateLightLevel(cycle, time) >= DayLevel ? CFLightLevel.Bright : CFLightLevel.Dim;
        }

        var color = ambient.AmbientLightColor;
        var brightness = 0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
        return brightness >= BrightAmbient ? CFLightLevel.Bright : brightness >= DimAmbient ? CFLightLevel.Dim : CFLightLevel.Darkness;
    }

    /// <summary>Something that stops light stands between the two points. Creatures do not.</summary>
    private bool Blocked(MapId map, Vector2 from, Vector2 to, float distance)
    {
        if (distance < 0.01f)
            return false;

        var ray = new CollisionRay(from, (to - from) / distance, (int) CollisionGroup.Opaque);
        foreach (var _ in _physics.IntersectRayWithPredicate(map, ray, distance, uid => !_occluders.TryComp(uid, out var occluder) || !occluder.Enabled))
            return true;

        return false;
    }
}
