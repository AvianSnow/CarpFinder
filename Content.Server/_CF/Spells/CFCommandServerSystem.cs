using System.Numerics;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.HTN;
using Content.Shared.CF.Spells;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.CF.Spells;

/// <summary>
/// Carries out Command's movement orders with GOAP steering. A creature with a GOAP brain plans the <c>Obey</c> action,
/// whose step calls <see cref="Drive"/>. A stock HTN mob has its HTN switched off for the order and is driven from
/// here. A creature with a player in it only gets told the order: nothing moves a player.
/// </summary>
public sealed partial class CFCommandServerSystem : EntitySystem
{
    /// <summary>How close to the caster counts as having approached, in tiles.</summary>
    private const float ApproachTiles = 1.5f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<CFCommandedComponent> ent, ref ComponentStartup args)
    {
        if (HasComp<CFGOAPComponent>(ent) || HasComp<ActorComponent>(ent) || !TryComp(ent, out HTNComponent? htn))
            return;

        // Stock HTN would fight the steering, so it sleeps for the order. The NPC stays active, so steering still runs.
        ent.Comp.PausedHtn = htn.Enabled;
        _htn.SetHTNEnabled((ent, htn), false);
        EnsureComp<CFGOAPSteeringComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<CFCommandedComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.PausedHtn || TerminatingOrDeleted(ent))
            return;

        _steering.Stop(ent.Owner);
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFCommandedComponent>();
        while (query.MoveNext(out var uid, out var order))
        {
            if (now >= order.Until || !_mobState.IsAlive(uid) || TerminatingOrDeleted(order.Commander))
            {
                RemCompDeferred<CFCommandedComponent>(uid);
                continue;
            }

            // A brain runs its own Obey step.
            if (order.PausedHtn)
                Drive((uid, order));
        }
    }

    /// <summary>Moves the creature one tick further on its order. Call it every tick while the order lasts.</summary>
    public void Drive(Entity<CFCommandedComponent> ent)
    {
        var commander = _transform.GetWorldPosition(ent.Comp.Commander);
        switch (ent.Comp.Order)
        {
            case CFCommandOrder.Approach:
                var near = Vector2.Distance(_transform.GetWorldPosition(ent), commander) <= ApproachTiles;
                if (near)
                {
                    HoldStill(ent);
                    return;
                }

                ent.Comp.Holding = false;
                _steering.Navigate(ent, commander, ent.Comp.Commander, alone: true);
                return;
            case CFCommandOrder.Flee:
                _steering.Flee(ent, commander);
                return;
            case CFCommandOrder.Halt:
                HoldStill(ent);
                return;
        }
    }

    private void HoldStill(Entity<CFCommandedComponent> ent)
    {
        if (ent.Comp.Holding)
            return;

        ent.Comp.Holding = true;
        _steering.Hold(ent);
    }
}
