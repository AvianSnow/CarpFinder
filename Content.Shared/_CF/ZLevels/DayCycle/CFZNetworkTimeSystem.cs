// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Shared.CF.ZLevels.DayCycle;

/// <summary>
/// A subsystem that connects LightCycle/SunShadowCycle with ZLevelSystem. Allows you to control the in-game
/// time of day for the entire z-network at once, which correctly propagates to ambient lighting and
/// wall sun-shadows on every map of the network.
/// </summary>
public sealed partial class CFZNetworkTimeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedLightCycleSystem _lightCycle = default!;

    /// <summary>
    /// Sets the current time of day (position within the light cycle, measured from its start) for every
    /// map in the given zNetwork.
    /// </summary>
    public void SetTime(Entity<CFZMapNetworkComponent?> network, TimeSpan time)
    {
        if (!Resolve(network, ref network.Comp))
            return;

        foreach (var (_, map) in network.Comp.ZLevels)
        {
            if (map is not { } mapUid || !TryComp<LightCycleComponent>(mapUid, out var lightCycle))
                continue;

            var elapsed = _timing.CurTime
                .Subtract(_ticker.RoundStartTimeSpan)
                .Subtract(_meta.GetPauseTime(mapUid));

            var newOffset = Wrap(time - elapsed, lightCycle.Duration);
            _lightCycle.SetOffset((mapUid, lightCycle), newOffset);
        }
    }

    /// <summary>
    /// Adds (or, if negative, subtracts) the given amount of time to the current time of day for every
    /// map in the given zNetwork.
    /// </summary>
    public void AddTime(Entity<CFZMapNetworkComponent?> network, TimeSpan delta)
    {
        if (!Resolve(network, ref network.Comp))
            return;

        foreach (var (_, map) in network.Comp.ZLevels)
        {
            if (map is not { } mapUid || !TryComp<LightCycleComponent>(mapUid, out var lightCycle))
                continue;

            var newOffset = Wrap(lightCycle.Offset + delta, lightCycle.Duration);
            _lightCycle.SetOffset((mapUid, lightCycle), newOffset);
        }
    }

    /// <summary>
    /// Wraps <paramref name="value"/> into the range [0, <paramref name="duration"/>).
    /// </summary>
    private static TimeSpan Wrap(TimeSpan value, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var ticks = value.Ticks % duration.Ticks;
        if (ticks < 0)
            ticks += duration.Ticks;

        return TimeSpan.FromTicks(ticks);
    }
}
