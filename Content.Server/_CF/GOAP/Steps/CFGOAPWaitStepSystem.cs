using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Stays put until thinking changes the plan.</summary>
public sealed partial class CFGOAPWaitStepSystem : CFGOAPStepSystem
{
    protected override CFGOAPStep Kind => CFGOAPStep.Wait;
}
