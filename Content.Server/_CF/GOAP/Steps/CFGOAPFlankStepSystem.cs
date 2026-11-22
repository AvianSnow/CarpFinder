using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Goes to this creature's side of the target, opposite a pack mate.</summary>
public sealed partial class CFGOAPFlankStepSystem : CFGOAPStepSystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPEncounterSystem _encounters = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Flank;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        MoveToFlank(ent);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (_timing.CurTime >= ent.Comp.NextFlankUpdate)
        {
            ent.Comp.NextFlankUpdate = _timing.CurTime + UpdateInterval;
            MoveToFlank(ent);
        }

        return CFGOAPSystem.Knows(ent.Comp, CFGOAPFact.AtFlank);
    }

    /// <summary>Heads for this creature's side of the target, or straight at it if a slot is blocked.</summary>
    private void MoveToFlank(Entity<CFGOAPComponent> ent)
    {
        if (ent.Comp.Target is not { } target
            || TerminatingOrDeleted(target)
            || !_encounters.TryGetGroup(ent, out var group)
            || !TryComp(ent, out CFGOAPSteeringComponent? steering))
            return;

        var at = _transform.GetMapCoordinates(target);
        var slot = _encounters.SlotAt(ent, group, target);
        if (steering.Obstacle == null && Steering.IsWalkable(ent, slot) && Steering.CanWalkStraight(ent, slot.Position))
        {
            steering.UsingField = false;
            Steering.MoveTo(ent, slot.Position);
            return;
        }

        // Seen through glass or round a corner, the way there is not the way it looks.
        if (steering.Obstacle != null || !Steering.CanWalkStraight(ent, at.Position))
        {
            Steering.Navigate(ent, at.Position, target);
            return;
        }

        steering.UsingField = false;
        Steering.Chase(ent, target);
    }
}
