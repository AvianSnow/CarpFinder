using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.Interaction;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Raises the shield it holds through use in hand, the same path a player takes, so it costs the same 1 action. It waits
/// for that action, and is finished once the shield is up or there is no shield left to raise.
/// </summary>
public sealed partial class CFGOAPRaiseShieldStepSystem : CFGOAPStepSystem
{
    private const int RaiseActions = 1;

    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFShieldSystem _shields = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.RaiseShield;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // Use in hand lowers a raised shield, so never use one that is already up.
        if (!_shields.TryGetUsableShield(ent, out var shield) || shield.Comp.Raised)
            return true;

        if (!_actions.CanAffordActions(ent, RaiseActions))
            return false;

        _interaction.UseInHandInteraction(ent, shield);
        return shield.Comp.Raised;
    }
}
