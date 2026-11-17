// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.Weather;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.ZLevels.Weather;

/// <summary>
/// A subsystem that connects WeatherSystem with ZLevelSystem. Allows you to control the weather for the entire z-network at once.
/// </summary>
public sealed partial class CFWeatherSystem : EntitySystem
{
    [Dependency] private SharedWeatherSystem _weather = default!;

    public void SetWeather(Entity<CFZMapNetworkComponent?> network, EntProtoId? proto, TimeSpan? duration)
    {
        if (!Resolve(network, ref network.Comp))
            return;

        foreach (var (_, map) in network.Comp.ZLevels)
        {
            if (!TryComp<MapComponent>(map, out var mapComp))
                continue;

            _weather.TrySetWeather(mapComp.MapId, proto, out _, duration);
        }
    }
}
