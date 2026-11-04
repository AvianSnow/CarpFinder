using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Factions;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Hazards;

/// <summary>
/// Hidden hazards: noticing them (Perception against the Stealth DC), setting them off, and disabling them.
/// What a hazard does when it goes off is its <c>CFCheck</c> and <c>CFDegreeEffects</c>, resolved by
/// <see cref="CFEffectSystem"/> with the hazard as both source and user.
/// </summary>
public sealed partial class CFHazardSystem : EntitySystem
{
    private static readonly VerbCategory DisarmCategory = new("cf-hazard-disarm", null);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFHazardComponent> ent, ref MapInitEvent args)
    {
        if (!_net.IsServer)
            return;

        var detect = ent.Comp.DetectRadiusFeet / CFAreaGrid.FeetPerTile;
        var trigger = Math.Max(0.1f, ent.Comp.TriggerSizeFeet / CFAreaGrid.FeetPerTile / 2f - 0.1f);
        CreateSensor(ent, CFHazardComponent.DetectFixture, detect);
        CreateSensor(ent, CFHazardComponent.TriggerFixture, trigger);

        // A body spawned with no fixtures has collision turned off; the sensors need it on.
        if (TryComp(ent, out PhysicsComponent? body))
        {
            _physics.SetCanCollide(ent, true, body: body);
            _physics.WakeBody(ent, body: body);
        }
    }

    private void CreateSensor(EntityUid uid, string id, float radius)
    {
        if (radius <= 0f || _fixtures.GetFixtureOrNull(uid, id) != null)
            return;

        _fixtures.TryCreateFixture(
            uid,
            new PhysShapeCircle(radius),
            id,
            density: 0f,
            hard: false,
            collisionLayer: (int) CollisionGroup.None,
            collisionMask: (int) CollisionGroup.MobLayer);
    }

    /// <summary>The sensors only ever touch living creatures that are not the hazard's allies.</summary>
    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<CFHazardComponent> ent, ref PreventCollideEvent args)
    {
        var ours = ReferenceEquals(args.OurFixture, _fixtures.GetFixtureOrNull(ent, CFHazardComponent.DetectFixture))
                   || ReferenceEquals(args.OurFixture, _fixtures.GetFixtureOrNull(ent, CFHazardComponent.TriggerFixture));
        if (ours && (!ent.Comp.Armed || !IsVictim(ent, args.OtherEntity)))
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<CFHazardComponent> ent, ref StartCollideEvent args)
    {
        if (!_net.IsServer || !ent.Comp.Armed)
            return;

        switch (args.OurFixtureId)
        {
            case CFHazardComponent.DetectFixture:
                TryNotice(ent, args.OtherEntity);
                break;
            case CFHazardComponent.TriggerFixture:
                Trigger(ent, args.OtherEntity);
                break;
        }
    }

    /// <summary>A living creature that is not an ally of whoever laid the hazard.</summary>
    public bool IsVictim(EntityUid hazard, EntityUid creature)
    {
        if (!TryComp(creature, out MobStateComponent? mob) || _mobState.IsDead(creature, mob))
            return false;

        return _factions.CanHarm(hazard, creature);
    }

    /// <summary>True when <paramref name="viewer"/> can see the hazard: it is revealed, they noticed it, or it is theirs.</summary>
    public bool CanSee(Entity<CFHazardComponent> hazard, EntityUid viewer)
    {
        return hazard.Comp.Revealed
               || hazard.Comp.DetectedBy.Contains(viewer)
               || !_factions.CanHarm(hazard, viewer);
    }

    /// <summary>
    /// One Perception check against the Stealth DC. The result shows over the creature for everyone nearby.
    /// Returns true when it noticed the hazard.
    /// </summary>
    public bool TryNotice(Entity<CFHazardComponent> hazard, EntityUid creature)
    {
        if (!hazard.Comp.Rolled.Add(creature) || !IsVictim(hazard, creature))
            return false;

        var modifier = CFDefense.PerceptionModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(creature),
            CompOrNull<CFAbilityScoreComponent>(creature),
            _bonuses.Total(creature, CFBonusStat.Perception),
            _conditions.Frightened(creature));
        var d20 = _random.Next(1, 21);
        var degree = CFDegrees.DegreeOfSuccess(d20, modifier, hazard.Comp.StealthDc);
        var noticed = degree.IsSuccess();

        PopupRoll(creature, "Perception", d20 + modifier, noticed);
        if (!noticed)
            return false;

        hazard.Comp.DetectedBy.Add(creature);
        Dirty(hazard);
        _popup.PopupEntity(Loc.GetString("cf-hazard-noticed", ("hazard", hazard.Owner)), creature, creature, PopupType.Medium);
        return true;
    }

    /// <summary>
    /// Sets the hazard off on <paramref name="victim"/>. It is revealed to everyone and stops working.
    /// </summary>
    public void Trigger(Entity<CFHazardComponent> hazard, EntityUid victim)
    {
        if (!hazard.Comp.Armed || !IsVictim(hazard, victim))
            return;

        hazard.Comp.Armed = false;
        hazard.Comp.Revealed = true;
        Dirty(hazard);

        _popup.PopupEntity(
            Loc.GetString("cf-hazard-triggered", ("hazard", hazard.Owner), ("target", Identity.Entity(victim, EntityManager))),
            victim,
            PopupType.MediumCaution);
        _effects.ResolveTarget(hazard, hazard, victim);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFHazardComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.Armed || ent.Comp.Disarm.Count == 0)
            return;

        if (!CanSee(ent, args.User))
            return;

        var user = args.User;
        if (ent.Comp.Disarm.Count == 1)
        {
            var only = ent.Comp.Disarm[0];
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("cf-hazard-disarm"),
                Message = Loc.GetString("cf-hazard-disarm-option", ("skill", only.Skill), ("dc", only.Dc)),
                Act = () => TryDisarm(ent, user, only),
            });
            return;
        }

        foreach (var option in ent.Comp.Disarm)
        {
            var picked = option;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("cf-hazard-disarm-option", ("skill", picked.Skill), ("dc", picked.Dc)),
                Category = DisarmCategory,
                Act = () => TryDisarm(ent, user, picked),
            });
        }
    }

    /// <summary>
    /// Disable a Device: the chosen skill against its DC. Success awards XP (<see cref="CFHazardDisarmedEvent"/>)
    /// and removes the hazard.
    /// Failure sets it off on the creature trying.
    /// </summary>
    public void TryDisarm(Entity<CFHazardComponent> hazard, EntityUid user, CFHazardDisarm option)
    {
        if (!_net.IsServer || !hazard.Comp.Armed || TerminatingOrDeleted(hazard))
            return;

        if (!_actions.TrySpendActions(user, hazard.Comp.DisarmActions))
            return;

        var degree = _conditions.RollSkillAgainst(user, option.Skill, option.Ability, option.Dc, out var total);
        var success = degree.IsSuccess();
        PopupRoll(user, option.Skill, total, success);

        if (!success)
        {
            Trigger(hazard, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString("cf-hazard-disarmed", ("hazard", hazard.Owner)), user, PopupType.Medium);
        var disarmed = new CFHazardDisarmedEvent(user, hazard.Comp.Level, hazard.Comp.Complex);
        RaiseLocalEvent(hazard, ref disarmed);

        hazard.Comp.Armed = false;
        Dirty(hazard);
        QueueDel(hazard);
    }

    /// <summary>"Name: rolled Perception 14 — Success" over the creature, for everyone who can see it.</summary>
    private void PopupRoll(EntityUid creature, string skill, int total, bool success)
    {
        _popup.PopupEntity(
            Loc.GetString(
                success ? "cf-hazard-roll-success" : "cf-hazard-roll-failure",
                ("name", Identity.Entity(creature, EntityManager)),
                ("skill", skill),
                ("total", total)),
            creature,
            success ? PopupType.Medium : PopupType.MediumCaution);
    }
}
