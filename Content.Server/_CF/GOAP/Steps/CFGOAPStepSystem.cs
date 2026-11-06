using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// What one <see cref="CFGOAPStep"/> does. Each kind is its own system, so it has its own dependencies and subscriptions,
/// and the enum in an action prototype says which system runs it. What a creature is in the middle of goes on its
/// components, because one system serves them all.
/// </summary>
public abstract partial class CFGOAPStepSystem : EntitySystem
{
    [Dependency] protected CFGOAPSystem Goap = default!;
    [Dependency] protected CFGOAPSteeringSystem Steering = default!;

    protected abstract CFGOAPStep Kind { get; }

    public override void Initialize()
    {
        base.Initialize();
        Goap.AddStep(Kind, this);
    }

    /// <summary>Called once, when the step starts.</summary>
    public virtual void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
    }

    /// <summary>
    /// Runs the step a little further. Returns true when it is finished. By default it never is: fighting, fleeing and
    /// following run until thinking changes the plan.
    /// </summary>
    public virtual bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        return false;
    }

    /// <summary>Called once when it stops, whether it finished or the plan changed. Steering has its own cleanup.</summary>
    public virtual void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
    }
}
