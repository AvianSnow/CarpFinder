using Content.Server.CF.GOAP.Sensors;
using Content.Shared.CF.GOAP;
using Content.Shared.Interaction;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Uses a held item of the kind the action names, over and over until it is gone or the plan changes.</summary>
public sealed partial class CFGOAPUseItemStepSystem : CFGOAPStepSystem
{
    private static readonly TimeSpan UseInterval = TimeSpan.FromSeconds(3);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private CFGOAPItemSensorSystem _items = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.UseItem;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!_items.TryFindHeld(ent, action.Item, out var item))
            return true;

        var now = _timing.CurTime;
        if (now < ent.Comp.StepTime)
            return false;

        ent.Comp.StepTime = now + UseInterval;
        _interaction.UseInHandInteraction(ent, item);
        return false;
    }
}
