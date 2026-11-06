using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Goes to the last known position, or as near as its personality dares, and looks around for a while.</summary>
public sealed partial class CFGOAPGoToLkpStepSystem : CFGOAPStepSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.GoToLkp;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Lkp is { } lkp)
            Steering.MoveTo(ent, lkp.Position);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Lkp is not { } lkp)
            return true;

        var profile = Goap.Profile(ent);
        var here = _transform.GetMapCoordinates(ent);
        if ((lkp.Position - here.Position).Length() > CFGOAPSystem.LkpArrival + profile.LkpKeepOut)
        {
            Steering.Travel(ent, lkp.Position);
            ent.Comp.StepTime = _timing.CurTime;
            return false;
        }

        Steering.Stop(ent.Owner);
        if (_timing.CurTime - ent.Comp.StepTime < TimeSpan.FromSeconds(profile.LkpDwell))
            return false;

        if (action.ForgetsLkp)
            ent.Comp.Lkp = null;

        return true;
    }
}
