using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Items;
using Content.Shared.CF.NPC;
using Content.Shared.CombatMode;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.NPC;

/// <summary>
/// Goal-oriented planning for advanced NPCs, on top of HTN. <see cref="CFGoapOperator"/> calls <see cref="Tick"/>.
/// It never cheats: sensors use sight, the influence maps, the last known position, and nodes.
/// </summary>
public sealed partial class CFGoapSystem : EntitySystem
{
    private const float SightTiles = 12f;

    private const float ItemTiles = 8f;

    private const float NodeTiles = 12f;

    private const float AllyTiles = 6f;

    private const float ReachTiles = 1.25f;

    /// <summary>How near the last known position counts as there.</summary>
    private const float LkpArrival = 1f;

    private const float PickUpReach = 1.2f;

    private const float ClosetReach = 1.5f;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan UseInterval = TimeSpan.FromSeconds(3);

    /// <summary>A noise takes a minute to fade from the map, but only startles for this long.</summary>
    private static readonly TimeSpan NoiseMemory = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFInfluenceMapSystem _influence = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;
    [Dependency] private CFStealthSystem _stealth = default!;
    [Dependency] private CFSteeringSystem _steering = default!;

    /// <summary>Gets a brain ready to run.</summary>
    public void Start(Entity<CFGoapComponent> ent)
    {
        if (Transform(ent).GridUid is { } grid)
            _influence.EnsureMap(grid);

        var steering = EnsureComp<CFSteeringComponent>(ent);
        steering.SpeedMultiplier = Profile(ent).MaxSpeed;

        // Each think is timed from the last one, so this keeps a group that woke together from thinking on one tick.
        ent.Comp.NextThink = _timing.CurTime + ThinkInterval * _random.NextDouble();
    }

    /// <summary>Stops whatever it was doing.</summary>
    public void Shutdown(Entity<CFGoapComponent> ent)
    {
        EndStep(ent);
        ent.Comp.Plan.Clear();
        ent.Comp.Goal = null;
    }

    public void Tick(Entity<CFGoapComponent> ent)
    {
        var now = _timing.CurTime;
        if (now >= ent.Comp.NextThink)
        {
            ent.Comp.NextThink = now + ThinkInterval;
            Think(ent);
        }

        RunStep(ent);
    }

    private CFGoapProfilePrototype Profile(Entity<CFGoapComponent> ent)
    {
        return _prototypes.Index(ent.Comp.Profile);
    }

    /// <summary>Reads the world, then replans if the goal changed or a fact flipped.</summary>
    public void Think(Entity<CFGoapComponent> ent)
    {
        var before = ent.Comp.Facts;
        ent.Comp.Facts = Sense(ent);
        CheckReflexes(ent);

        var goal = ChooseGoal(ent, out var plan);
        if (goal == ent.Comp.Goal && before == ent.Comp.Facts && ent.Comp.Step < ent.Comp.Plan.Count)
            return;

        // A plan that still starts the same way keeps running, so a fact flipping does not restart a walk.
        var sameStep = ent.Comp.Step < ent.Comp.Plan.Count
                       && plan.Count > 0
                       && ent.Comp.Plan[ent.Comp.Step] == plan[0];
        if (!sameStep)
            EndStep(ent);

        ent.Comp.Goal = goal;
        ent.Comp.Plan = plan;
        ent.Comp.Step = 0;
    }

    /// <summary>What the creature can tell right now.</summary>
    private uint Sense(Entity<CFGoapComponent> ent)
    {
        var uid = ent.Owner;
        var profile = Profile(ent);
        CFGoapFact facts = 0;

        var here = _transform.GetMapCoordinates(uid);
        if (TryFindVisibleEnemy(ent, here, out var seen) || TryGetFelt(uid, out seen))
        {
            seen = PackTarget(uid, seen);
            ent.Comp.Target = seen;
            ent.Comp.NodesChecked = 0;
            ent.Comp.Lkp = _transform.GetMapCoordinates(seen);
            ent.Comp.LkpHeading = CompOrNull<PhysicsComponent>(seen)?.LinearVelocity ?? Vector2.Zero;
            facts |= CFGoapFact.TargetVisible;

            if ((_transform.GetMapCoordinates(seen).Position - here.Position).Length() <= ReachTiles)
                facts |= CFGoapFact.TargetInReach;
        }

        if (ent.Comp.Lkp is { } lkp && lkp.MapId == here.MapId)
        {
            facts |= CFGoapFact.KnowsLkp;
            if ((lkp.Position - here.Position).Length() <= LkpArrival + profile.LkpKeepOut)
                facts |= CFGoapFact.AtLkp;
        }

        if (HoldsWeapon(uid))
            facts |= CFGoapFact.HasWeapon;
        if (TryFindHeld(uid, CFItemUse.Heals, out _))
            facts |= CFGoapFact.HasHealingItem;
        if (TryFindItem(uid, here, CFItemUse.Weapon, out _))
            facts |= CFGoapFact.WeaponNearby;
        if (TryFindItem(uid, here, CFItemUse.Heals, out _))
            facts |= CFGoapFact.HealingNearby;
        if (_influence.TryFollow(uid, CFInfluenceLayer.Scent, ent.Comp.Target, out _))
            facts |= CFGoapFact.ScentNearby;
        if (ent.Comp.NodesChecked < profile.NodesToCheck && TryPickNode(ent, out _))
            facts |= CFGoapFact.NodeNearby;
        if (HasAlliesNearby(uid, here))
            facts |= CFGoapFact.AlliesNearby;
        if (_actions.CanAffordActions(uid, 1))
            facts |= CFGoapFact.ActionsLeft;
        if (Health(uid) < profile.HurtHealth)
            facts |= CFGoapFact.Hurt;
        if (_conditions.IsProne(uid))
            facts |= CFGoapFact.Prone;
        if (_conditions.Frightened(uid) > 0)
            facts |= CFGoapFact.Frightened;

        facts |= PackFacts(ent, here, (facts & CFGoapFact.TargetVisible) != 0);
        return (uint) facts;
    }

    /// <summary>An enemy its rays touched, which they only do in plain view.</summary>
    private bool TryGetFelt(EntityUid uid, out EntityUid felt)
    {
        felt = CompOrNull<CFSteeringComponent>(uid)?.Felt ?? default;
        return felt.IsValid() && !TerminatingOrDeleted(felt);
    }

    /// <summary>The nearest enemy it can really see. An undetected creature is not seen, however close it is.</summary>
    private bool TryFindVisibleEnemy(EntityUid uid, MapCoordinates here, out EntityUid target)
    {
        target = default;
        var nearest = float.MaxValue;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, SightTiles))
        {
            if (mob.Owner == uid
                || !_mobState.IsAlive(mob)
                || !_factions.CanHarm(uid, mob)
                || _stealth.Detection(mob, uid) == CFDetection.Undetected)
                continue;

            var distance = (_transform.GetMapCoordinates(mob).Position - here.Position).Length();
            if (distance >= nearest || !_examine.InRangeUnOccluded(uid, mob, SightTiles))
                continue;

            nearest = distance;
            target = mob;
        }

        return nearest < float.MaxValue;
    }

    private float Health(EntityUid uid)
    {
        return TryComp(uid, out CFCombatantComponent? combat) && combat.MaxHp > 0
            ? (float) combat.CurrentHp / combat.MaxHp
            : 1f;
    }

    private bool HoldsWeapon(EntityUid uid)
    {
        if (TryComp(uid, out CFCombatOptionsComponent? style) && style.NaturalDie > 0)
            return true;

        // A monster's own attack, such as a stat block's bite.
        if (HasComp<CFMeleeWeaponComponent>(uid))
            return true;

        return TryFindHeld(uid, CFItemUse.Weapon, out _);
    }

    private bool TryFindHeld(EntityUid uid, CFItemUse use, out EntityUid item)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (TryComp(held, out CFItemUseComponent? data) && (data.Uses & use) != 0)
            {
                item = held;
                return true;
            }
        }

        item = default;
        return false;
    }

    private bool TryFindItem(EntityUid uid, MapCoordinates here, CFItemUse use, out EntityUid item)
    {
        item = default;
        var nearest = float.MaxValue;
        foreach (var candidate in _lookup.GetEntitiesInRange<CFItemUseComponent>(here, ItemTiles))
        {
            if ((candidate.Comp.Uses & use) == 0 || _containers.IsEntityInContainer(candidate))
                continue;

            var at = _transform.GetMapCoordinates(candidate);
            if (_steering.IsDangerous(at))
                continue;

            var distance = (at.Position - here.Position).Length();
            if (distance >= nearest || !_examine.InRangeUnOccluded(uid, candidate.Owner, ItemTiles))
                continue;

            nearest = distance;
            item = candidate;
        }

        return nearest < float.MaxValue;
    }

    private bool HasAlliesNearby(EntityUid uid, MapCoordinates here)
    {
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, AllyTiles))
        {
            if (mob.Owner != uid && _mobState.IsAlive(mob) && !_factions.CanHarm(uid, mob))
                return true;
        }

        return false;
    }

    private void CheckReflexes(Entity<CFGoapComponent> ent)
    {
        var now = _timing.CurTime;
        foreach (var reflex in Profile(ent).Reflexes)
        {
            if (!Triggered(ent, reflex))
                continue;

            ent.Comp.ForcedGoal = reflex.Goal;
            ent.Comp.ForcedUntil = now + TimeSpan.FromSeconds(reflex.Seconds);
        }
    }

    private bool Triggered(Entity<CFGoapComponent> ent, CFSteeringReflex reflex)
    {
        switch (reflex.Trigger)
        {
            case CFGoapTrigger.HealthBelow:
                return Health(ent) < reflex.Value;
            case CFGoapTrigger.TargetSeen:
                return (ent.Comp.Facts & (uint) CFGoapFact.TargetVisible) != 0;
            case CFGoapTrigger.NoiseNearby:
                return _influence.HasRecentMark(ent, CFInfluenceLayer.Noise, NoiseMemory);
            case CFGoapTrigger.AllyDown:
                return NoticeFallenAlly(ent);
            default:
                return false;
        }
    }

    private bool NoticeFallenAlly(Entity<CFGoapComponent> ent)
    {
        var noticed = false;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(_transform.GetMapCoordinates(ent), AllyTiles))
        {
            if (mob.Owner != ent.Owner
                && !_mobState.IsAlive(mob)
                && !_factions.CanHarm(ent, mob)
                && ent.Comp.FallenAllies.Add(mob))
                noticed = true;
        }

        return noticed;
    }

    /// <summary>The best goal that a plan exists for. A forced goal comes first while it lasts.</summary>
    private ProtoId<CFGoapGoalPrototype>? ChooseGoal(Entity<CFGoapComponent> ent, out List<ProtoId<CFGoapActionPrototype>> plan)
    {
        var profile = Profile(ent);
        var options = Options(ent, profile);
        var health = Health(ent);
        plan = new List<ProtoId<CFGoapActionPrototype>>();
        ProtoId<CFGoapGoalPrototype>? best = null;
        var bestPriority = float.MinValue;

        foreach (var id in ent.Comp.Goals)
        {
            var goal = _prototypes.Index(id);
            var priority = Priority(ent, goal, profile, health);
            if (priority <= bestPriority || !Applies(ent, goal, profile, health))
                continue;

            var conditions = CFGoapConditions.From(goal.Wants, goal.Avoids);
            var steps = CFGoapPlanner.Plan(ent.Comp.Facts, conditions, options);
            if (steps == null || steps.Count == 0)
                continue;

            bestPriority = priority;
            best = id;
            plan.Clear();
            foreach (var step in steps)
            {
                plan.Add(ent.Comp.Actions[step]);
            }
        }

        return best;
    }

    private float Priority(Entity<CFGoapComponent> ent, CFGoapGoalPrototype goal, CFGoapProfilePrototype profile, float health)
    {
        if (ent.Comp.ForcedGoal == goal.ID && _timing.CurTime < ent.Comp.ForcedUntil)
            return float.MaxValue;

        var priority = goal.Priority + goal.WhenHurt * (1f - health);
        return priority * profile.GoalPriority.GetValueOrDefault(goal.ID, 1f);
    }

    private bool Applies(Entity<CFGoapComponent> ent, CFGoapGoalPrototype goal, CFGoapProfilePrototype profile, float health)
    {
        if (ent.Comp.ForcedGoal == goal.ID && _timing.CurTime < ent.Comp.ForcedUntil)
            return true;

        if (goal.BelowFleeHealth && health >= profile.FleeHealth)
            return false;

        return CFGoapConditions.From(goal.OnlyWhen, goal.NotWhen).Unmet(ent.Comp.Facts) == 0;
    }

    private List<CFGoapOption> Options(Entity<CFGoapComponent> ent, CFGoapProfilePrototype profile)
    {
        var options = new List<CFGoapOption>(ent.Comp.Actions.Count);
        for (var i = 0; i < ent.Comp.Actions.Count; i++)
        {
            var id = ent.Comp.Actions[i];
            var action = _prototypes.Index(id);
            var cost = action.Cost * profile.ActionCost.GetValueOrDefault(id, 1f);
            options.Add(new CFGoapOption(
                i,
                CFGoapConditions.From(action.Needs, action.NeedsNot),
                CFGoapConditions.From(action.Gives, action.Removes),
                cost));
        }

        return options;
    }

    private void RunStep(Entity<CFGoapComponent> ent)
    {
        if (ent.Comp.Step >= ent.Comp.Plan.Count)
            return;

        var action = _prototypes.Index(ent.Comp.Plan[ent.Comp.Step]);
        if (!ent.Comp.StepStarted)
        {
            ent.Comp.StepStarted = true;
            ent.Comp.StepTime = _timing.CurTime;
            StartStep(ent, action);
        }

        if (!UpdateStep(ent, action))
            return;

        EndStep(ent);
        ent.Comp.Step++;
    }

    private List<CFSteeringBehavior> Steer(CFGoapProfilePrototype profile, params CFSteeringBehavior[] behaviors)
    {
        var list = new List<CFSteeringBehavior>(behaviors);
        if (profile.WallFollow > 0f)
            list.Add(new CFSteeringBehavior { Kind = CFSteeringKind.WallFollow, Weight = profile.WallFollow });

        if (profile.Separation > 0f)
            list.Add(new CFSteeringBehavior { Kind = CFSteeringKind.Separation, Weight = profile.Separation });

        return list;
    }

    private void StartStep(Entity<CFGoapComponent> ent, CFGoapActionPrototype action)
    {
        var profile = Profile(ent);
        var uid = ent.Owner;
        switch (action.Step)
        {
            case CFGoapStep.Strike:
                if (ent.Comp.Target is { } target)
                {
                    _combatMode.SetInCombatMode(uid, true);
                    EnsureComp<NPCMeleeCombatComponent>(uid).Target = target;
                    _steering.Steer(uid, new List<CFSteeringBehavior> { new() { Kind = CFSteeringKind.Hold } });
                }

                break;
            case CFGoapStep.Close:
                if (ent.Comp.Target is { } quarry)
                    Chase(ent, quarry);

                break;
            case CFGoapStep.Flank:
                MoveToFlank(ent);
                break;
            case CFGoapStep.Flee:
                // NPC movement raises no move input, so stealth cannot notice this on its own.
                _stealth.Reveal(uid);
                _steering.Steer(uid, Steer(profile, new CFSteeringBehavior { Kind = CFSteeringKind.Evade }), ent.Comp.Target, ent.Comp.Lkp?.Position, CFFeltMobs.Avoid);
                break;
            case CFGoapStep.FollowScent:
                _steering.Steer(uid, Steer(profile, new CFSteeringBehavior { Kind = CFSteeringKind.FieldFollow }), ent.Comp.Target, felt: CFFeltMobs.Chase);
                break;
            case CFGoapStep.Wander:
                _steering.Steer(uid, Steer(profile, new CFSteeringBehavior { Kind = CFSteeringKind.Wander, Jitter = profile.WanderJitter }));
                break;
            case CFGoapStep.Regroup:
                _steering.Steer(uid, Steer(profile, new CFSteeringBehavior { Kind = CFSteeringKind.Cohesion, Radius = AllyTiles }));
                break;
            case CFGoapStep.PickUp:
                if (TryFindItem(uid, _transform.GetMapCoordinates(uid), action.Item, out var item))
                {
                    ent.Comp.Item = item;
                    MoveTo(ent, _transform.GetWorldPosition(item));
                }

                break;
            case CFGoapStep.CheckNode:
                if (TryPickNode(ent, out var node))
                {
                    ent.Comp.Node = node;
                    MoveTo(ent, _transform.GetWorldPosition(node));
                }

                break;
            case CFGoapStep.GoToLkp:
                if (ent.Comp.Lkp is { } lkp)
                    MoveTo(ent, lkp.Position);

                break;
        }
    }

    /// <summary>
    /// Steers straight at a point and lets the feelers turn it aside, with no pathfinding at all.
    /// An enemy a ray touches on the way pulls it in.
    /// </summary>
    private void MoveTo(Entity<CFGoapComponent> ent, Vector2 point)
    {
        var profile = Profile(ent);
        var arrive = new CFSteeringBehavior { Kind = CFSteeringKind.Arrive, Radius = profile.ArriveRadius };
        _steering.Steer(ent, Steer(profile, arrive), point: point, felt: CFFeltMobs.Chase);
    }

    /// <summary>The same for a creature, aiming for where it is heading.</summary>
    private void Chase(Entity<CFGoapComponent> ent, EntityUid target)
    {
        var pursuit = new CFSteeringBehavior { Kind = CFSteeringKind.Pursuit };
        _steering.Steer(ent, Steer(Profile(ent), pursuit), target, felt: CFFeltMobs.Chase);
    }

    /// <summary>Runs the step a little further. Returns true when it is finished.</summary>
    private bool UpdateStep(Entity<CFGoapComponent> ent, CFGoapActionPrototype action)
    {
        var uid = ent.Owner;
        var here = _transform.GetMapCoordinates(uid);
        switch (action.Step)
        {
            case CFGoapStep.Close:
                if (ent.Comp.Target is { } quarry && !TerminatingOrDeleted(quarry))
                    Navigate(ent, _transform.GetWorldPosition(quarry), quarry);

                return (ent.Comp.Facts & (uint) CFGoapFact.TargetInReach) != 0;
            case CFGoapStep.Flank:
                if (_timing.CurTime >= ent.Comp.NextFlankUpdate)
                {
                    ent.Comp.NextFlankUpdate = _timing.CurTime + FlankUpdateInterval;
                    MoveToFlank(ent);
                }

                return (ent.Comp.Facts & (uint) CFGoapFact.AtFlank) != 0;
            case CFGoapStep.GoToLkp:
                return UpdateGoToLkp(ent, here);
            case CFGoapStep.PickUp:
                return UpdatePickUp(ent, here);
            case CFGoapStep.UseItem:
                return UpdateUseItem(ent, action);
            case CFGoapStep.CheckNode:
                return UpdateCheckNode(ent, here);
            case CFGoapStep.Maneuver:
                _maneuvers.TryUse(uid, action.Maneuver, ent.Comp.Target);
                return true;
            default:
                // Fighting, fleeing and following run until thinking changes the plan.
                return false;
        }
    }

    private bool UpdateGoToLkp(Entity<CFGoapComponent> ent, MapCoordinates here)
    {
        if (ent.Comp.Lkp is not { } lkp)
            return true;

        var profile = Profile(ent);
        if ((lkp.Position - here.Position).Length() > LkpArrival + profile.LkpKeepOut)
        {
            Navigate(ent, lkp.Position, null);
            ent.Comp.StepTime = _timing.CurTime;
            return false;
        }

        _steering.Stop(ent.Owner);
        return _timing.CurTime - ent.Comp.StepTime >= TimeSpan.FromSeconds(profile.LkpDwell);
    }

    private bool UpdatePickUp(Entity<CFGoapComponent> ent, MapCoordinates here)
    {
        if (ent.Comp.Item is not { } item || TerminatingOrDeleted(item) || _containers.IsEntityInContainer(item))
            return true;

        var itemAt = _transform.GetMapCoordinates(item).Position;
        if ((itemAt - here.Position).Length() > PickUpReach)
        {
            Navigate(ent, itemAt, null);
            return false;
        }

        _hands.TryPickupAnyHand(ent, item);
        return true;
    }

    private bool UpdateUseItem(Entity<CFGoapComponent> ent, CFGoapActionPrototype action)
    {
        if (!TryFindHeld(ent, action.Item, out var item))
            return true;

        var now = _timing.CurTime;
        if (now < ent.Comp.StepTime)
            return false;

        ent.Comp.StepTime = now + UseInterval;
        _interaction.UseInHandInteraction(ent, item);
        return false;
    }

    private bool UpdateCheckNode(Entity<CFGoapComponent> ent, MapCoordinates here)
    {
        if (ent.Comp.Node is not { } node || TerminatingOrDeleted(node))
            return true;

        var at = _transform.GetMapCoordinates(node);
        if ((at.Position - here.Position).Length() > LkpArrival)
        {
            Navigate(ent, at.Position, null);
            return false;
        }

        _steering.Stop(ent.Owner);
        Search(ent, node, at);
        return true;
    }

    /// <summary>Opens what is here if it is a closet, then Seeks. Hiders are only found by this, never known.</summary>
    private void Search(Entity<CFGoapComponent> ent, EntityUid node, MapCoordinates at)
    {
        if (TryComp(node, out CFInvestigationNodeComponent? data) && (data.Tags & CFNodeTag.Closet) != 0)
        {
            foreach (var closet in _lookup.GetEntitiesInRange<EntityStorageComponent>(at, ClosetReach))
            {
                _storage.OpenStorage((closet.Owner, closet.Comp), ent);
            }
        }

        _stealth.TrySeek(ent, at);
        ent.Comp.Checked[node] = _timing.CurTime;
        ent.Comp.NodesChecked++;
        ent.Comp.Node = null;
    }

    /// <summary>
    /// A weighted random node around the last known position, so it is not predictable. Reachable means in sight of
    /// the creature or linked from the node it just checked. Pack mates each take a different one.
    /// </summary>
    public bool TryPickNode(Entity<CFGoapComponent> ent, out EntityUid node)
    {
        node = default;
        if (ent.Comp.Lkp is not { } lkp)
            return false;

        var profile = Profile(ent);
        var now = _timing.CurTime;
        var total = 0f;
        var scored = new List<(EntityUid Node, float Score)>();
        foreach (var candidate in _lookup.GetEntitiesInRange<CFInvestigationNodeComponent>(lkp, NodeTiles))
        {
            if (IsTakenByPack(ent, candidate))
                continue;

            // A coward watches from a distance, and that includes not searching up close.
            var at = _transform.GetMapCoordinates(candidate);
            var position = at.Position;
            if ((position - lkp.Position).Length() < profile.LkpKeepOut || _steering.IsDangerous(at))
                continue;

            var since = ent.Comp.Checked.TryGetValue(candidate, out var when)
                ? (float) (now - when).TotalSeconds
                : float.MaxValue;
            var score = CFNodeScore.Score(
                candidate.Comp.Weight,
                position - lkp.Position,
                ent.Comp.LkpHeading,
                HasScent(ent, candidate),
                since,
                TagMultiplier(profile, candidate.Comp.Tags));

            if (score <= 0f)
                continue;

            scored.Add((candidate, score));
            total += score;
        }

        if (scored.Count == 0)
            return false;

        var pick = _random.NextFloat(0f, total);
        foreach (var (candidate, score) in scored)
        {
            node = candidate;
            pick -= score;
            if (pick <= 0f)
                break;
        }

        return true;
    }

    private bool HasScent(Entity<CFGoapComponent> ent, EntityUid node)
    {
        if (Transform(node).GridUid is not { } grid)
            return false;

        var cell = _influence.CellAt(grid, CFInfluenceLayer.Scent, Transform(node).Coordinates);
        return cell.Intensity > 0f && (ent.Comp.Target == null || cell.Emitter == ent.Comp.Target);
    }

    private static float TagMultiplier(CFGoapProfilePrototype profile, CFNodeTag tags)
    {
        var multiplier = 1f;
        foreach (var (tag, value) in profile.NodeTags)
        {
            if ((tags & tag) != 0)
                multiplier *= value;
        }

        return multiplier;
    }

    private bool IsTakenByPack(Entity<CFGoapComponent> ent, EntityUid node)
    {
        var query = EntityQueryEnumerator<CFGoapComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (uid != ent.Owner && other.Node == node && !_factions.CanHarm(ent, uid))
                return true;
        }

        return false;
    }

    private void EndStep(Entity<CFGoapComponent> ent)
    {
        ent.Comp.StepStarted = false;
        ent.Comp.UsingField = false;
        ent.Comp.Item = null;
        ent.Comp.Node = null;
        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Stop(ent.Owner);
    }
}
