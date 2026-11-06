using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Walks back to where it started, however far that is.</summary>
public sealed partial class CFGOAPGoHomeStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.GoHome;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        ent.Comp.GoingHome = true;
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!Goap.TryGetHome(ent, out var home))
            return true;

        if ((home.Position - _transform.GetMapCoordinates(ent).Position).Length() <= CFGOAPSystem.HomeArrival)
        {
            Steering.Stop(ent.Owner);
            return true;
        }

        Steering.Travel(ent, home.Position, alone: true);
        return false;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        ent.Comp.GoingHome = false;
    }
}
