using Content.Server.CF.GOAP.Encounters;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CF;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Spells;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sleep;

/// <summary>
/// Puts a GOAP brain to sleep while no living player is within its <see cref="CFGOAPComponent.WakeFeet"/>, and wakes
/// it when one comes in. It only sleeps once no player has been near for <see cref="CFGOAPComponent.SleepDelay"/>, so
/// a player stepping in and out of range does not flip it each time.
/// Sleeping is stock <see cref="NPCSystem.SleepNPC"/>: HTN, the plan, steering and sensors all stop, and the creature
/// stands where it is. It keeps its target and last known position, so it picks up where it was on waking.
/// </summary>
public sealed partial class CFGOAPSleepSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private CFCommandSystem _command = default!;
    [Dependency] private CFGOAPEncounterSystem _encounters = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The cvar, cached. Settings are not game state.</summary>
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, CFCVars.GoapSleep, value => _enabled = value, true);
    }

    /// <summary>A new creature waits out the delay before it can sleep, as if a player had just left.</summary>
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFGOAPComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.LastWoken = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled)
            return;

        var now = _timing.CurTime;
        List<MapCoordinates>? players = null;
        var query = EntityQueryEnumerator<CFGOAPComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var goap, out var htn))
        {
            if (now < goap.NextSleepCheck)
                continue;

            goap.NextSleepCheck = now + CheckInterval;
            if (!Manages(uid))
                continue;

            players ??= PlayerPositions();
            if (IsPlayerNear(uid, goap, players))
            {
                goap.LastWoken = now;
                if (!HasComp<ActiveNPCComponent>(uid))
                    _npc.WakeNPC(uid, htn);
                continue;
            }

            if (HasComp<ActiveNPCComponent>(uid) && now >= goap.LastWoken + goap.SleepDelay && !IsBusy((uid, goap)))
                _npc.SleepNPC(uid, htn);
        }
    }

    /// <summary>
    /// Wakes it now, and the rest of its group with it, as if a player had come near. Being hurt by something it
    /// could not see does this, so a mob shot from past its range still answers.
    /// </summary>
    public void Wake(EntityUid uid)
    {
        if (!_enabled)
            return;

        if (_encounters.TryGetGroup(uid, out var group))
        {
            foreach (var member in group.Members)
            {
                WakeOne(member);
            }
        }

        WakeOne(uid);
    }

    private void WakeOne(EntityUid uid)
    {
        if (!TryComp(uid, out CFGOAPComponent? goap) || !TryComp(uid, out HTNComponent? htn) || !Manages(uid))
            return;

        goap.LastWoken = _timing.CurTime;
        if (!HasComp<ActiveNPCComponent>(uid))
            _npc.WakeNPC(uid, htn);
    }

    /// <summary>
    /// A body with a player in it, or one that is down, is left to stock: it sleeps those itself, and waking one would
    /// fight it.
    /// </summary>
    private bool Manages(EntityUid uid)
    {
        if (HasComp<ActorComponent>(uid) || _mobState.IsIncapacitated(uid))
            return false;

        return !TryComp(uid, out MindContainerComponent? mind) || !mind.HasMind;
    }

    /// <summary>Something it is in the middle of, which would look wrong frozen. It sleeps once that ends.</summary>
    private bool IsBusy(Entity<CFGOAPComponent> ent)
    {
        return CFGOAPSystem.Knows(ent.Comp, CFGOAPFact.TargetVisible)
               || CFGOAPSystem.Knows(ent.Comp, CFGOAPFact.OnFire)
               || _command.Current(ent) != CFCommandOrder.None;
    }

    private bool IsPlayerNear(EntityUid uid, CFGOAPComponent goap, List<MapCoordinates> players)
    {
        if (players.Count == 0)
            return false;

        var here = _transform.GetMapCoordinates(uid);
        var range = goap.WakeFeet / CFAreaGrid.FeetPerTile;
        foreach (var player in players)
        {
            if (player.MapId == here.MapId && (player.Position - here.Position).LengthSquared() <= range * range)
                return true;
        }

        return false;
    }

    /// <summary>Only a living body with a player in it counts, so a ghost or an aghost wakes nothing.</summary>
    private List<MapCoordinates> PlayerPositions()
    {
        var positions = new List<MapCoordinates>();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player
                || !TryComp(player, out MobStateComponent? mob)
                || _mobState.IsDead(player, mob))
                continue;

            positions.Add(_transform.GetMapCoordinates(player));
        }

        return positions;
    }
}
