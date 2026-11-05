using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server.CF.ZLevels;

/// <summary>
/// A stack of maps that count as levels of one place. Each level is its own map, so nothing crosses
/// between them except through stairs, ladders and holes. Based on CP14's <c>CP14StationZLevelsComponent</c>.
/// </summary>
[RegisterComponent]
public sealed partial class CFStationZLevelsComponent : Component
{
    /// <summary>The level of the station's own map.</summary>
    [DataField]
    public int DefaultLevel;

    /// <summary>Other levels to load, by level number. Lower numbers are further down.</summary>
    [DataField]
    public Dictionary<int, ResPath> Levels = new();

    /// <summary>How far apart levels are, for falling.</summary>
    [DataField]
    public int LevelHeightFeet = 10;

    [ViewVariables]
    public Dictionary<MapId, int> Maps = new();
}
