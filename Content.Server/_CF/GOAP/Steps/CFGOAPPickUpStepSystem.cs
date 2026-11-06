using Content.Server.CF.GOAP.Sensors;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Walks to the nearest item of the kind the action names and picks it up.</summary>
public sealed partial class CFGOAPPickUpStepSystem : CFGOAPStepSystem
{
    private const float PickUpReach = 1.2f;

    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPItemSensorSystem _items = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.PickUp;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!_items.TryFindItem(ent, _transform.GetMapCoordinates(ent), action.Item, out var item))
            return;

        ent.Comp.Item = item;
        Steering.MoveTo(ent, _transform.GetWorldPosition(item));
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Item is not { } item || TerminatingOrDeleted(item) || _containers.IsEntityInContainer(item))
            return true;

        var itemAt = _transform.GetMapCoordinates(item).Position;
        if ((itemAt - _transform.GetMapCoordinates(ent).Position).Length() > PickUpReach)
        {
            Steering.Navigate(ent, itemAt, null);
            return false;
        }

        _hands.TryPickupAnyHand(ent, item);
        return true;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        ent.Comp.Item = null;
    }
}
