using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.NPC;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.NPC;

/// <summary>
/// Spawns encounters and keeps track of each group. Plain mobs elsewhere keep stock AI:
/// a creature only gets a GOAP brain here, because it is part of an encounter.
/// </summary>
public sealed partial class CFEncounterSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How far from the spawner the group appears, in tiles.</summary>
    private const float SpawnSpread = 1f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    [SubscribeLocalEvent]
    private void OnInit(Entity<CFEncounterSpawnerComponent> ent, ref ComponentInit args)
    {
        _links.EnsureSourcePorts(
            ent,
            CFEncounterSpawnerComponent.DiedPort,
            CFEncounterSpawnerComponent.RoutedPort,
            CFEncounterSpawnerComponent.ClearedPort);
    }

    /// <summary>Polled, so a radius edited in VV works at once. See <c>CFSceneSystem</c>.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFEncounterSpawnerComponent>();
        while (query.MoveNext(out var uid, out var spawner))
        {
            if (!spawner.Armed || now < spawner.NextCheck)
                continue;

            spawner.NextCheck = now + CheckInterval;
            if (IsAdventurerNear((uid, spawner)))
                Spawn((uid, spawner));
        }
    }

    /// <summary>Only a living body with a player in it counts, so a ghost or an aghost sets nothing off.</summary>
    private bool IsAdventurerNear(Entity<CFEncounterSpawnerComponent> ent)
    {
        var here = _transform.GetMapCoordinates(ent);
        var range = ent.Comp.TriggerFeet / CFAreaGrid.FeetPerTile;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player
                || !TryComp(player, out MobStateComponent? mob)
                || _mobState.IsDead(player, mob))
                continue;

            var there = _transform.GetMapCoordinates(player);
            if (there.MapId == here.MapId && (there.Position - here.Position).Length() <= range)
                return true;
        }

        return false;
    }

    /// <summary>Spawns the group now. A spawner holds one group at a time, so a new one replaces its memory of the last.</summary>
    public void Spawn(Entity<CFEncounterSpawnerComponent> ent)
    {
        ent.Comp.Armed = false;
        if (!_prototypes.TryIndex<CFEncounterPrototype>(ent.Comp.Encounter, out var encounter))
        {
            Log.Warning($"{ToPrettyString(ent)} has an unknown encounter: {ent.Comp.Encounter}");
            return;
        }

        var mobs = MobList(ent, encounter);
        if (mobs.Count == 0)
            return;

        var count = ent.Comp.Count > 0 ? ent.Comp.Count : encounter.Count;
        ent.Comp.Members.Clear();
        ent.Comp.Target = null;
        ent.Comp.Routed = false;
        ent.Comp.Field.Clear();
        ent.Comp.Total = count;
        ent.Comp.RoutAt = encounter.RoutAt;

        for (var i = 0; i < count; i++)
        {
            var mob = Spawn(mobs[i % mobs.Count], SpawnPoint(ent, i, count));
            Enlist(ent, mob, encounter);
        }
    }

    private List<string> MobList(Entity<CFEncounterSpawnerComponent> ent, CFEncounterPrototype encounter)
    {
        var mobs = new List<string>();
        if (string.IsNullOrWhiteSpace(ent.Comp.Mobs))
        {
            foreach (var mob in encounter.Mobs)
            {
                mobs.Add(mob);
            }

            return mobs;
        }

        foreach (var name in ent.Comp.Mobs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (_prototypes.HasIndex<EntityPrototype>(name))
                mobs.Add(name);
            else
                Log.Warning($"{ToPrettyString(ent)} lists an unknown mob: {name}");
        }

        return mobs;
    }

    /// <summary>Around the spawner, or on it where a wall is in the way.</summary>
    private MapCoordinates SpawnPoint(EntityUid spawner, int index, int count)
    {
        var centre = _transform.GetMapCoordinates(spawner);
        var angle = MathF.Tau * index / Math.Max(1, count);
        var point = centre.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * SpawnSpread);

        if (Transform(spawner).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return centre;

        var tile = _maps.WorldToTile(grid, gridComp, point.Position);
        return _turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable) ? centre : point;
    }

    /// <summary>Gives a creature the group's brain, whatever it is. This is what lets any mob be chosen.</summary>
    private void Enlist(Entity<CFEncounterSpawnerComponent> ent, EntityUid mob, CFEncounterPrototype encounter)
    {
        ent.Comp.Members.Add(mob);
        EnsureComp<CFEncounterMemberComponent>(mob).Group = ent;

        var goap = EnsureComp<CFGoapComponent>(mob);
        goap.Profile = encounter.Profile;
        goap.Goals.AddRange(encounter.Goals);
        goap.Actions.AddRange(encounter.Actions);
        EnsureComp<CFSteeringComponent>(mob);

        if (!TryComp(mob, out HTNComponent? htn))
            return;

        htn.RootTask = new HTNCompoundTask { Task = "CFGoapCompound" };
        _htn.Replan(htn);
    }

    /// <summary>Broadcast: <c>MobStateComponent</c> and this event are already taken as a directed pair.</summary>
    [SubscribeLocalEvent]
    private void OnMobState(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            MemberDown(args.Target);
    }

    [SubscribeLocalEvent]
    private void OnMemberShutdown(Entity<CFEncounterMemberComponent> ent, ref ComponentShutdown args)
    {
        MemberDown(ent);
    }

    /// <summary>Every member sends a signal as it falls, and the group breaks once few enough are left.</summary>
    private void MemberDown(EntityUid uid)
    {
        if (!TryComp(uid, out CFEncounterMemberComponent? member)
            || !TryComp(member.Group, out CFEncounterSpawnerComponent? group)
            || !group.Members.Remove(uid))
            return;

        var spawner = member.Group;
        _links.InvokePort(spawner, CFEncounterSpawnerComponent.DiedPort);

        if (group.Members.Count == 0)
        {
            _links.InvokePort(spawner, CFEncounterSpawnerComponent.ClearedPort);
            return;
        }

        if (group.Routed || group.Members.Count > group.Total * group.RoutAt)
            return;

        group.Routed = true;
        _links.InvokePort(spawner, CFEncounterSpawnerComponent.RoutedPort);
    }
}
