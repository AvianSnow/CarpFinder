// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Flight.Components;
using Content.Shared.DoAfter;
using Content.Shared.Toggleable;
using Robust.Shared.Analyzers;

namespace Content.Shared.CF.ZLevels.Flight;

public abstract partial class CFSharedZFlightSystem
{

    [SubscribeLocalEvent]
    private void OnControllableFlightStopped(Entity<CFControllableFlightComponent> ent, ref CFFlightStoppedEvent args)
    {
        _actions.SetEnabled(ent.Comp.ZLevelDownActionEntity, false);
        _actions.SetEnabled(ent.Comp.ZLevelUpActionEntity, false);

        // Update toggle action icon state
        if (ent.Comp.ZLevelToggleActionEntity != null)
            _actions.SetToggled(ent.Comp.ZLevelToggleActionEntity, false);
    }

    [SubscribeLocalEvent]
    private void OnControllableFlightStarted(Entity<CFControllableFlightComponent> ent, ref CFFlightStartedEvent args)
    {
        _actions.SetEnabled(ent.Comp.ZLevelDownActionEntity, true);
        _actions.SetEnabled(ent.Comp.ZLevelUpActionEntity, true);

        // Update toggle action icon state
        if (ent.Comp.ZLevelToggleActionEntity != null)
            _actions.SetToggled(ent.Comp.ZLevelToggleActionEntity, true);
    }

    [SubscribeLocalEvent]
    private void OnZLevelUp(Entity<CFControllableFlightComponent> ent, ref CFZFlightActionUp args)
    {
        if (args.Handled)
            return;

        var map = Transform(ent).MapUid;
        if (map is null)
            return;

        if (!TryComp<CFZFlyerComponent>(ent, out var flyerComp))
            return;

        if (!_zLevel.TryMapUp(map.Value, out var mapAbove))
            return;

        flyerComp.TargetMapHeight = mapAbove.Comp.Depth;
        DirtyField(ent, flyerComp, nameof(CFZFlyerComponent.TargetMapHeight));

        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnZLevelDown(Entity<CFControllableFlightComponent> ent, ref CFZFlightActionDown args)
    {
        if (args.Handled)
            return;

        var map = Transform(ent).MapUid;
        if (map is null)
            return;

        if (!TryComp<CFZFlyerComponent>(ent, out var flyerComp))
            return;

        if (!_zLevel.TryMapDown(map.Value, out var mapBelow))
            return;

        flyerComp.TargetMapHeight = mapBelow.Comp.Depth;
        DirtyField(ent, flyerComp, nameof(CFZFlyerComponent.TargetMapHeight));

        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnZLevelToggle(Entity<CFControllableFlightComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.Action.Owner != ent.Comp.ZLevelToggleActionEntity)
            return;

        if (!TryComp<CFZFlyerComponent>(ent, out var flyerComp))
            return;

        if (flyerComp.Active)
        {
            DeactivateFlight((ent, flyerComp));
        }
        else
        {
            // If StartFlightDoAfter is set, start a doAfter before activating flight
            if (ent.Comp.StartFlightDoAfter != null)
            {
                //Preventive start flying visuals
                StartFlightVisuals((ent, flyerComp));

                var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.StartFlightDoAfter.Value, new CFStartFlightDoAfterEvent(), ent)
                {
                    BreakOnMove = false,
                    BlockDuplicate = true,
                    BreakOnDamage = true,
                    CancelDuplicate = true,
                };

                _doAfter.TryStartDoAfter(doAfter);
            }
            else
            {
                // No delay, activate flight immediately
                TryActivateFlight((ent, flyerComp));
            }
        }

        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnStartFlightDoAfter(Entity<CFControllableFlightComponent> ent, ref CFStartFlightDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
        {
            StopFlightVisuals(ent.Owner);
            return;
        }

        TryActivateFlight(ent.Owner);
        args.Handled = true;
    }
}
