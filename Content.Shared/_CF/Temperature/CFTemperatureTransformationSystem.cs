using Content.Shared.CF.Cooking;
using Content.Shared.Temperature;
using Robust.Shared.Network;

namespace Content.Shared.CF.Temperature;

/// <summary>
/// Turns a thing into another when its temperature enters one of its ranges: dough held over a fire becomes bread.
/// The result takes its place, in a hand or a store or on the floor. After CP14's temperature system.
/// </summary>
public sealed partial class CFTemperatureTransformationSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private CFCookingSystem _cooking = default!;

    [SubscribeLocalEvent]
    private void OnTemperatureChanged(Entity<CFTemperatureTransformationComponent> ent, ref TemperatureChangedEvent args)
    {
        if (_net.IsClient || !ent.Comp.AutoTransformOnCooked)
            return;

        foreach (var entry in ent.Comp.Entries)
        {
            if (args.CurrentTemperature < entry.TemperatureRange.X || args.CurrentTemperature >= entry.TemperatureRange.Y)
                continue;

            if (entry.TransformTo is { } result)
                _cooking.Replace(ent, result);

            return;
        }
    }

    /// <summary>Whether heat turns this into something else at all.</summary>
    public static bool IsCookable(CFTemperatureTransformationComponent comp)
    {
        if (!comp.AutoTransformOnCooked)
            return false;

        foreach (var entry in comp.Entries)
        {
            if (entry.TransformTo != null)
                return true;
        }

        return false;
    }
}
