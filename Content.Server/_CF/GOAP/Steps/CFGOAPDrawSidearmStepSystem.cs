using Content.Server.CF.GOAP.Sensors;
using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Lets go of what it is shooting with, which is free, and Interacts to take its melee weapon out of its pocket, which
/// is 1 action. With the gun gone the standoff is over, so it needs no memory of having done this.
/// </summary>
public sealed partial class CFGOAPDrawSidearmStepSystem : CFGOAPStepSystem
{
    private const int DrawActions = 1;

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.DrawSidearm;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        Steering.Hold(ent);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out CFGOAPStandoffComponent? standoff)
            || !_inventory.TryGetSlotEntity(ent, standoff.SidearmSlot, out var sidearm))
            return true;

        if (!_actions.CanAffordActions(ent, DrawActions))
            return false;

        if (_gun.TryGetGun(ent, out var gun) && !_hands.TryDrop(ent.Owner, gun.Owner, checkActionBlocker: false))
            return true;

        if (!_inventory.TryUnequip(ent, standoff.SidearmSlot, silent: true, force: true))
            return true;

        // The hand the gun was in is the active one, which is the hand a swing uses.
        if (!_hands.TryPickup(ent, sidearm.Value, checkActionBlocker: false))
            _hands.TryPickupAnyHand(ent, sidearm.Value, checkActionBlocker: false);

        _actions.TrySpendActions(ent, DrawActions);
        return true;
    }
}
