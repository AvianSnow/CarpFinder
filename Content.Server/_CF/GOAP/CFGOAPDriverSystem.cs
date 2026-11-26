using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP;

/// <summary>
/// Runs every GOAP brain. A brain is awake while <see cref="CFGOAPComponent.Awake"/> is set, and each tick an awake
/// brain gets <see cref="CFGOAPSystem.Tick"/>. Stock HTN has no part in it: a
/// brain removes any <see cref="HTNComponent"/> its body brought, so a stock mob with a brain added thinks with the
/// brain alone. Who sleeps and wakes it: a player in the body puts it to sleep and leaving wakes it, as going down
/// and getting up do, and <c>CFGOAPSleepSystem</c> sleeps it while no player is near. A companion on a stock
/// steering trip pauses instead: it stays awake, so steering runs, but it does not think.
/// </summary>
public sealed partial class CFGOAPDriverSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFGOAPSystem _goap = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // npc.enabled stops every NPC, this one included, so tests can keep the world still.
        if (!_npc.Enabled)
            return;

        var query = EntityQueryEnumerator<CFGOAPComponent>();
        while (query.MoveNext(out var uid, out var goap))
        {
            if (!goap.Awake || goap.Paused)
                continue;

            _goap.Tick((uid, goap));
        }
    }

    /// <summary>
    /// A brain always runs the creature. With two parents the first one's <c>HTN</c> wins, so a mob with stock AI of its
    /// own would otherwise keep that and fight the brain, whatever <c>CFGOAPBrain</c> says.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInit(Entity<CFGOAPComponent> ent, ref ComponentInit args)
    {
        RemComp<HTNComponent>(ent);
    }

    /// <summary>A brain given to a creature already in the world, as an encounter does to a stock mob, starts at once.</summary>
    [SubscribeLocalEvent]
    private void OnStartup(Entity<CFGOAPComponent> ent, ref ComponentStartup args)
    {
        if (MetaData(ent).EntityLifeStage >= EntityLifeStage.MapInitialized)
            Wake((ent, ent.Comp));
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFGOAPComponent> ent, ref MapInitEvent args)
    {
        Wake((ent, ent.Comp));
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<CFGOAPComponent> ent, ref ComponentShutdown args)
    {
        Sleep((ent, ent.Comp));
    }

    [SubscribeLocalEvent]
    private void OnPlayerAttached(Entity<CFGOAPComponent> ent, ref PlayerAttachedEvent args)
    {
        Sleep((ent, ent.Comp));
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(Entity<CFGOAPComponent> ent, ref PlayerDetachedEvent args)
    {
        if (!TerminatingOrDeleted(ent))
            Wake((ent, ent.Comp));
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<CFGOAPComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            Wake((ent, ent.Comp));
        else
            Sleep((ent, ent.Comp));
    }

    public bool IsAwake(EntityUid uid)
    {
        return TryComp(uid, out CFGOAPComponent? goap) && goap.Awake;
    }

    /// <summary>
    /// Starts the brain, unless a player or a mind is in the body or it is down. True when it is awake afterwards.
    /// </summary>
    public bool Wake(Entity<CFGOAPComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !CanRun(ent))
            return false;

        ent.Comp.LastWoken = _timing.CurTime;
        if (ent.Comp.Awake)
            return true;

        ent.Comp.Awake = true;
        _goap.Start((ent, ent.Comp));
        return true;
    }

    /// <summary>Stops the brain where it stands. It keeps its target, last known position and home for when it wakes.</summary>
    public void Sleep(Entity<CFGOAPComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.Awake)
            return;

        _goap.Shutdown((ent, ent.Comp));
        ent.Comp.Awake = false;
    }

    /// <summary>A brain whose lists or profile just changed starts over, so it thinks on the new ones.</summary>
    public void Restart(Entity<CFGOAPComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!ent.Comp.Awake)
        {
            Wake(ent);
            return;
        }

        _goap.Shutdown((ent, ent.Comp));
        _goap.Start((ent, ent.Comp));
    }

    /// <summary>Stops thinking while something else walks the creature. Steering still runs, because it stays awake.</summary>
    public void Pause(Entity<CFGOAPComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || ent.Comp.Paused)
            return;

        ent.Comp.Paused = true;
        if (ent.Comp.Awake)
            _goap.Shutdown((ent, ent.Comp));
    }

    /// <summary>Thinks again, at once.</summary>
    public void Resume(Entity<CFGOAPComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.Paused)
            return;

        ent.Comp.Paused = false;
        if (!ent.Comp.Awake)
            return;

        _goap.Start((ent, ent.Comp));
        ent.Comp.NextThink = TimeSpan.Zero;
    }

    /// <summary>
    /// A body with a player in it is played, one with a mind and no player is away for a moment, and one that is down
    /// cannot act. None of them thinks.
    /// </summary>
    private bool CanRun(EntityUid uid)
    {
        if (HasComp<ActorComponent>(uid) || _mobState.IsIncapacitated(uid))
            return false;

        return !TryComp(uid, out MindContainerComponent? mind) || !mind.HasMind;
    }
}
