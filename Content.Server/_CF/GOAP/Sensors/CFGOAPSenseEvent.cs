using Content.Shared.CF.GOAP;
using Robust.Shared.Map;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// Raised on each think. Every sensor system handles it and adds the facts it can tell, so a new sensor is one new
/// system and nothing else changes. It is a broadcast, because a directed event allows only one system per component.
/// </summary>
[ByRefEvent]
public record struct CFGOAPSenseEvent(Entity<CFGOAPComponent> Creature, MapCoordinates Here, CFGOAPProfilePrototype Profile)
{
    public CFGOAPFact Facts;

    public readonly bool Has(CFGOAPFact fact)
    {
        return (Facts & fact) != 0;
    }
}
