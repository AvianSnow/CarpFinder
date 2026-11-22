using Content.Shared.GameTicking;
using Content.Server.Weather;
using Content.Shared.CF.Outdoors;
using Content.Shared.CF.Time;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.Outdoors;

/// <summary>What time of day it is, and whether something is out under the sky.</summary>
public sealed partial class CFOutdoorsSystem : EntitySystem
{
    public const string WorldClock = "CFWorldClock";
    public const string SkyWorld = "CFSkyWorld";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private WeatherSystem _weather = default!;

    /// <summary>
    /// Gives a generated map CF's breathable air and sky. The world is floating islands, not space,
    /// so every map generator calls this before it builds anything.
    /// </summary>
    public void ApplySkyWorld(EntityUid map)
    {
        EntityManager.AddComponents(map, _prototypes.Index<EntityPrototype>(SkyWorld));
    }

    public CFTimeComponent? Clock()
    {
        return _prototypes.Index<EntityPrototype>(WorldClock).TryComp(out CFTimeComponent? time, Factory) ? time : null;
    }

    /// <summary>Daylight by the world clock. A round starts at dawn. Without a clock it is always day.</summary>
    public bool IsDay()
    {
        return Clock()?.IsDay(_timing.CurTime - _ticker.RoundStartTimeSpan) ?? true;
    }

    /// <summary>On a map with an outdoor world, and with nothing overhead.</summary>
    public bool IsExposed(EntityUid uid)
    {
        var xform = Transform(uid);
        if (!HasComp<CFOutdoorsComponent>(xform.MapUid) || _container.IsEntityInContainer(uid))
            return false;

        if (xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return true;

        var tile = _maps.GetTileRef(grid, gridComp, xform.Coordinates);
        return _weather.CanWeatherAffect((grid, gridComp, null), tile);
    }

    public bool UnderSunlight(EntityUid uid)
    {
        return IsDay() && IsExposed(uid);
    }
}
