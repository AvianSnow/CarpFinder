using Content.Server.CF.GOAP.Sleep;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// Being hit tells it where the attacker is, which is how it reacts to something it never saw. It learns the place, not
/// the sight of them, so it goes to look. What it can already see comes first.
/// </summary>
public sealed partial class CFGOAPPainSensorSystem : EntitySystem
{
    /// <summary>How long it holds a hit against someone. One round.</summary>
    private static readonly TimeSpan GrudgeTime = TimeSpan.FromSeconds(6);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFGOAPSightSensorSystem _sight = default!;
    [Dependency] private CFGOAPSleepSystem _sleep = default!;

    [SubscribeLocalEvent]
    private void OnDamaged(Entity<CFGOAPComponent> ent, ref CFDamagedEvent args)
    {
        if (args.Source == ent.Owner
            || !HasComp<MobStateComponent>(args.Source)
            || !_factions.CanHarm(ent, args.Source))
            return;

        // A hit from past its wake range, such as an arrow, still gets an answer.
        _sleep.Wake(ent);
        ent.Comp.HurtBy = args.Source;
        ent.Comp.HurtByUntil = _timing.CurTime + GrudgeTime;
        if (CFGOAPSystem.Knows(ent.Comp, CFGOAPFact.TargetVisible))
            return;

        _sight.Follow(ent, args.Source);
        ent.Comp.NextThink = TimeSpan.Zero;
    }
}
