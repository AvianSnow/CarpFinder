using System.Numerics;
using Content.Shared.CF.Detection;
using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Runs along a flee field. If the field says it is already as safe as it can get and the threat is close, it is
/// cornered, which is a fact like any other: the plan moves on to whatever an action that needs it offers.
/// </summary>
public sealed partial class CFGOAPFleeStepSystem : CFGOAPStepSystem
{
    /// <summary>How near a threat has to be for having nowhere to go to count, in tiles.</summary>
    private const float CorneredTiles = 3f;

    private static readonly TimeSpan CorneredTime = TimeSpan.FromSeconds(2);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Flee;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // NPC movement raises no move input, so stealth cannot notice this on its own.
        _stealth.Reveal(ent);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // A reflex can send it running with nothing to run from.
        if (!TryGetThreat(ent, out var threat))
            return false;

        var wayOut = Steering.Flee(ent, threat);
        if (!wayOut && Vector2.Distance(_transform.GetWorldPosition(ent), threat) <= CorneredTiles)
            ent.Comp.CorneredUntil = _timing.CurTime + CorneredTime;

        return false;
    }

    private bool TryGetThreat(Entity<CFGOAPComponent> ent, out Vector2 threat)
    {
        if (ent.Comp.Target is { } target && !TerminatingOrDeleted(target))
        {
            threat = _transform.GetWorldPosition(target);
            return true;
        }

        threat = ent.Comp.Lkp?.Position ?? default;
        return ent.Comp.Lkp != null;
    }
}
