using Content.Shared.ActionBlocker;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Skill actions with no button: scream to Demoralize, and point in reach to Feint.
/// </summary>
public sealed partial class CFManeuverSystem
{
    private const string ScreamEmote = "Scream";

    private const string WhistleEmote = "Whistle";

    private const string FeintSkill = "Deception";

    private const float DemoralizeFeet = 30f;

    /// <summary>A little past the weapon's reach, because reach is measured from the edge of a square.</summary>
    private const float AimedStrikeSlackFeet = 2.5f;

    /// <summary>Stock's Alt+Z runs the first alternative verb, so this has to outrank wielding and the like.</summary>
    private const int FinisherVerbPriority = 100;

    /// <summary>Half of the 45° cone an aimed shout reaches.</summary>
    private const double AimHalfAngleDegrees = 22.5;

    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    /// <summary>
    /// Only the emote menu sends this. A typed emote or a scream of pain never reaches here, so neither spends an action.
    /// </summary>
    [EventSubscription]
    private void OnPlayEmote(PlayEmoteMessage msg, EntitySessionEventArgs args)
    {
        if (!_net.IsServer || args.SenderSession.AttachedEntity is not { } user)
            return;

        if (!_blocker.CanEmote(user))
            return;

        if (msg.ProtoId == ScreamEmote)
            TryAimedDemoralize(user);
        else if (msg.ProtoId == WhistleEmote && HasStats(user) && _combatMode.IsInCombatMode(user))
            _stealth.TryDiversion(user);
    }

    /// <summary>
    /// Demoralizes the enemy closest to where this creature is aiming. Combat mode turns a creature to the cursor,
    /// so its facing is its aim. With no one to frighten it is an ordinary scream and costs nothing.
    /// </summary>
    public bool TryAimedDemoralize(EntityUid user)
    {
        if (!HasStats(user) || !_combatMode.IsInCombatMode(user))
            return false;

        if (!TryAimedEnemy(user, DemoralizeFeet, demoralizing: true, out var target) || !_actions.TrySpendActions(user, 1))
            return false;

        return _conditions.TryDemoralize(user, target);
    }

    /// <summary>Stock's Alt+Z, with a melee weapon in hand. The verb only exists while the Swashbuckler has panache.</summary>
    [SubscribeLocalEvent]
    private void OnWeaponAltVerbs(Entity<CFMeleeWeaponComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;
        if (!_hands.IsHolding(user, ent)
            || !TryComp(user, out CFCombatOptionsComponent? style)
            || !style.PanacheStyle
            || !style.Panache)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cf-act-finisher"),
            Priority = FinisherVerbPriority,
            Act = () => TryFinisher(user),
        });
    }

    /// <summary>One action. Strikes the enemy in reach closest to where the creature aims, and spends its panache.</summary>
    public bool TryFinisher(EntityUid user)
    {
        if (!_net.IsServer || !TryComp(user, out CFCombatOptionsComponent? style) || !style.Panache)
            return false;

        var reach = CFFlanking.DefaultReachFeet;
        if (_hands.TryGetActiveItem(user, out var held))
            reach = _weapons.ReachFeet(held.Value);

        if (!TryAimedEnemy(user, reach + AimedStrikeSlackFeet, demoralizing: false, out var target)
            || !_actions.TrySpendActions(user, 1))
            return false;

        _activities.Finisher(user, target);
        return true;
    }

    private bool TryAimedEnemy(EntityUid user, float feet, bool demoralizing, out EntityUid target)
    {
        target = default;
        var here = _transform.GetMapCoordinates(user);
        var facing = _transform.GetWorldRotation(user);
        var tiles = feet / CFAreaGrid.FeetPerTile;
        var best = double.MaxValue;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, tiles))
        {
            var uid = mob.Owner;
            if (uid == user
                || !_mobState.IsAlive(uid)
                || !_factions.CanHarm(user, uid)
                || demoralizing && !_conditions.CanDemoralize(user, uid))
                continue;

            var toTarget = _transform.GetMapCoordinates(uid).Position - here.Position;
            var off = Math.Abs(Angle.ShortestDistance(facing, toTarget.ToWorldAngle()).Degrees);
            if (off > AimHalfAngleDegrees || off >= best || !_interaction.InRangeUnobstructed(user, uid, tiles + 1f))
                continue;

            best = off;
            target = uid;
        }

        return best < double.MaxValue;
    }

    /// <summary>
    /// What pointing at something does. A class mark comes first. In combat mode, an enemy in reach that is
    /// already marked, or a character with no mark, is Feinted. Everything else stays a free gesture.
    /// </summary>
    public void PointAt(Entity<CFCombatOptionsComponent> ent, EntityUid target)
    {
        if (!TryMark(ent, target))
            TryPointFeint(ent, target);
    }

    private bool TryPointFeint(EntityUid user, EntityUid target)
    {
        if (!_combatMode.IsInCombatMode(user)
            || !_mobState.IsAlive(target)
            || !_factions.CanHarm(user, target)
            || !CanFeint(user, target))
            return false;

        return _actions.TrySpendActions(user, 1) && _conditions.TryFeint(user, target);
    }

    /// <summary>Feint needs trained Deception and the target in melee reach.</summary>
    private bool CanFeint(EntityUid user, EntityUid target)
    {
        if (!TryComp(user, out CFCombatantComponent? combat)
            || CFDefense.SkillRank(_prototypes, combat, FeintSkill) < CFProficiencyRank.Trained)
            return false;

        var reach = CFFlanking.DefaultReachFeet;
        if (_hands.TryGetActiveItem(user, out var held))
            reach = _weapons.ReachFeet(held.Value);

        return InReach(user, target, reach);
    }
}
