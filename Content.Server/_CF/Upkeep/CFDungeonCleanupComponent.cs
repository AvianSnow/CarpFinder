using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.CF.Upkeep;

/// <summary>
/// Goes on a gameMap's station. Every <see cref="Interval"/> the dungeon wizard clears loose items, puddles and
/// bodies from every running map, after announcing it at each of <see cref="Warnings"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFDungeonCleanupComponent : Component
{
    [DataField]
    public TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How long before the cleanup each warning is announced, longest first.</summary>
    [DataField]
    public List<TimeSpan> Warnings = new()
    {
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromSeconds(10),
    };

    /// <summary>Anything created this soon after its map counts as placed by the mapper, and is never cleaned.</summary>
    [DataField]
    public TimeSpan MapperGrace = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? NextCleanup;

    /// <summary>How many of <see cref="Warnings"/> have been announced for <see cref="NextCleanup"/>.</summary>
    [DataField]
    public int WarningsGiven;
}
