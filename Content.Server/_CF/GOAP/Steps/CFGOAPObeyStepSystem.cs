using Content.Server.CF.Spells;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Spells;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Carries out a Command order through <see cref="CFCommandServerSystem.Drive"/>, until the order ends.</summary>
public sealed partial class CFGOAPObeyStepSystem : CFGOAPStepSystem
{
    [Dependency] private CFCommandServerSystem _command = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Obey;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // Thinking stopped whatever steering it had, so a hold has to be started again.
        if (TryComp(ent, out CFCommandedComponent? order))
            order.Holding = false;
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out CFCommandedComponent? order))
            return true;

        _command.Drive((ent, order));
        return false;
    }
}
