using Content.Server.CF.GOAP.Influence;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs.Components;

namespace Content.Server.CF.GOAP;

/// <summary>A personality's snap reactions, which force a goal for a while.</summary>
public sealed partial class CFGOAPSystem
{
    private const float FallenAllyTiles = 6f;

    /// <summary>A noise takes a minute to fade from the map, but only startles for this long.</summary>
    private static readonly TimeSpan NoiseMemory = TimeSpan.FromSeconds(1);

    private void CheckReflexes(Entity<CFGOAPComponent> ent)
    {
        var now = _timing.CurTime;
        foreach (var reflex in Profile(ent).Reflexes)
        {
            if (!Triggered(ent, reflex))
                continue;

            ent.Comp.ForcedGoal = reflex.Goal;
            ent.Comp.ForcedUntil = now + TimeSpan.FromSeconds(reflex.Seconds);
        }
    }

    private bool Triggered(Entity<CFGOAPComponent> ent, CFGOAPReflex reflex)
    {
        switch (reflex.Trigger)
        {
            case CFGOAPTrigger.HealthBelow:
                return Health(ent) < reflex.Value;
            case CFGOAPTrigger.TargetSeen:
                return Knows(ent.Comp, CFGOAPFact.TargetVisible);
            case CFGOAPTrigger.NoiseNearby:
                return _influence.HasRecentMark(ent, CFGOAPInfluenceLayer.Noise, NoiseMemory);
            case CFGOAPTrigger.AllyDown:
                return NoticeFallenAlly(ent);
            default:
                return false;
        }
    }

    private bool NoticeFallenAlly(Entity<CFGOAPComponent> ent)
    {
        var noticed = false;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(_transform.GetMapCoordinates(ent), FallenAllyTiles))
        {
            if (mob.Owner != ent.Owner
                && !_mobState.IsAlive(mob)
                && !_factions.CanHarm(ent, mob)
                && ent.Comp.FallenAllies.Add(mob))
                noticed = true;
        }

        return noticed;
    }
}
