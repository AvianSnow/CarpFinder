using Content.Server.Movement.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Movement.Pulling.Components;

namespace Content.Server.CF.Actions;

/// <summary>
/// Reposition on stock's move-pulled input. Stock raises no event for it, so this watches the component it adds.
/// Things that are not grabbed move for free, as they always have.
/// </summary>
public sealed partial class CFRepositionSystem : EntitySystem
{
    [Dependency] private CFManeuverSystem _maneuvers = default!;

    [SubscribeLocalEvent]
    private void OnMoveStarted(Entity<PullMovingComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out CFConditionComponent? cond)
            || !cond.Grabbed
            || !TryComp(ent, out PullableComponent? pullable)
            || pullable.Puller is not { } puller)
            return;

        if (!_maneuvers.TryReposition(puller, ent))
            RemCompDeferred<PullMovingComponent>(ent);
    }
}
