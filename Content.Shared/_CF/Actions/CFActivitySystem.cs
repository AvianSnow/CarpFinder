using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared.CF.Actions;

/// <summary>
/// The activities still on the action wedge. Most actions use stock inputs instead: see <see cref="CFManeuverSystem"/>.
/// Class features and chosen feats are the same kind of action, granted at spawn.
/// </summary>
public sealed partial class CFActivitySystem : EntitySystem
{
    /// <summary>How long Courageous Anthem's bonus lasts. Printed as 1 round; a Bard repeats it at this pace.</summary>
    public const float AnthemSeconds = 12f;

    /// <summary>A 60-foot emanation.</summary>
    private const float AnthemTiles = 12f;

    /// <summary>Stock's jump speed. A leap lands as soon as it has covered the distance.</summary>
    private const float LeapThrowSpeed = 10f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFTrapLayerSystem _traps = default!;
    [Dependency] private CFSpellSystem _spells = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    [SubscribeLocalEvent]
    private void OnSpellAttempt(Entity<CFSpellComponent> spell, ref ActionAttemptEvent args)
    {
        if (!TryComp(args.User, out CFCombatOptionsComponent? opt) || !opt.Raging)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-act-rage-concentrate");
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFActivityComponent> ent, ref ActionAttemptEvent args)
    {
        switch (ent.Comp.Kind)
        {
            case CFActivityKind.Rage
                when !TryComp(args.User, out CFCombatOptionsComponent? rage) || !rage.CanRage:
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-cannot");
                break;
            case CFActivityKind.Leap when !_maneuvers.CanUse(args.User, CFBasicAction.Leap):
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-leap-cannot");
                break;
            case CFActivityKind.ConstructTrap when !_traps.CanConstruct(args.User):
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-trap-cannot");
                break;
        }
    }

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFActivityComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        switch (ent.Comp.Kind)
        {
            case CFActivityKind.Rage:
                ToggleRage(args.Performer);
                break;
            case CFActivityKind.Anthem:
                Anthem(args.Performer);
                break;
            case CFActivityKind.Leap:
                Leap(args.Performer);
                break;
            case CFActivityKind.ConstructTrap:
                _traps.TryConstruct(args.Performer);
                break;
        }
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFActivityComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (_conditions.IsCalmed(args.Performer))
        {
            _popup.PopupEntity(Loc.GetString("cf-cond-calmed"), args.Performer, args.Performer);
            return;
        }

        switch (ent.Comp.Kind)
        {
            case CFActivityKind.Flurry:
                Flurry(args.Performer, args.Target);
                break;
            case CFActivityKind.Spellstrike:
                Spellstrike(args.Performer, args.Target);
                break;
            case CFActivityKind.SuddenCharge:
                StrikeHeld(args.Performer, args.Target);
                break;
        }
    }

    private void ToggleRage(EntityUid user)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? opt) || !opt.CanRage)
            return;

        opt.Raging = !opt.Raging;
        Dirty(user, opt);
        _popup.PopupEntity(Loc.GetString(opt.Raging ? "cf-act-rage-start" : "cf-act-rage-end"), user, user);
    }

    public void Mark(EntityUid user, EntityUid target, bool prey)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        if (prey)
        {
            opt.Prey = target;
            opt.PreyHitThisTurn = false;
            _popup.PopupEntity(Loc.GetString("cf-act-hunt-prey"), user, user);
        }
        else
        {
            opt.Exploited = target;
            _conditions.SetOffGuardTo(target, user, 60f);
            _popup.PopupEntity(Loc.GetString("cf-act-exploit"), user, user);
        }

        Dirty(user, opt);
    }

    public void Stratagem(EntityUid user, EntityUid target)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        opt.StratagemRoll = _random.Next(1, 21);
        opt.StratagemTarget = target;
        Dirty(user, opt);
        _popup.PopupEntity(Loc.GetString("cf-act-stratagem", ("roll", opt.StratagemRoll)), user, user);
    }

    private void Flurry(EntityUid user, EntityUid target)
    {
        var stats = _strikes.UnarmedStats(user);
        _strikes.PerformStrike(user, target, stats, user, melee: true);
        _strikes.PerformStrike(user, target, stats, user, melee: true);
    }

    private void Spellstrike(EntityUid user, EntityUid target)
    {
        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        var dice = 1;
        var die = 8;
        if (_spells.TrySpendAnySlot(user, 1))
        {
            dice = 2;
            die = 6;
        }

        var bonus = 0;
        for (var i = 0; i < dice; i++)
            bonus += _random.Next(1, die + 1);

        _strikes.PerformStrike(user, target, stats, weapon, melee: true, bonusDamage: bonus);
    }

    public void Finisher(EntityUid user, EntityUid target)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? opt) || !opt.Panache)
            return;

        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        // Spent first, so a finisher that drops its target can earn panache back.
        opt.Panache = false;
        Dirty(user, opt);
        _strikes.PerformStrike(user, target, stats, weapon, melee: true, finisher: true);
    }

    private void StrikeHeld(EntityUid user, EntityUid target)
    {
        if (_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            _strikes.PerformStrike(user, target, stats, weapon, melee: true);
    }

    /// <summary>Leap: 10 feet at Speed 15, 15 feet at Speed 30, and none below that.</summary>
    public static int LeapTiles(int speedFeet)
    {
        return speedFeet >= 30 ? 3 : speedFeet >= 15 ? 2 : 0;
    }

    /// <summary>Jumps the way the creature faces. Combat mode turns it to the cursor, so that is where it aims.</summary>
    public void Leap(EntityUid user)
    {
        var tiles = LeapTiles(CompOrNull<CFCombatantComponent>(user)?.SpeedFeet ?? 0);
        if (tiles == 0)
            return;

        var xform = Transform(user);
        var landing = xform.Coordinates.Offset(xform.LocalRotation.ToWorldVec() * tiles);
        _throwing.TryThrow(user, landing, LeapThrowSpeed, doSpin: false);
    }

    /// <summary>+1 status to attack rolls, damage rolls, and saves against fear for the Bard and nearby allies.</summary>
    public void Anthem(EntityUid user)
    {
        Buff(user);
        var origin = _transform.GetMapCoordinates(user);
        foreach (var uid in _lookup.GetEntitiesInRange(origin, AnthemTiles))
        {
            if (uid == user || !HasComp<MobStateComponent>(uid) || _factions.CanHarm(user, uid))
                continue;

            Buff(uid);
        }

        _popup.PopupEntity(Loc.GetString("cf-act-anthem"), user, user);
    }

    private void Buff(EntityUid ally)
    {
        _bonuses.AddBonus(ally, CFBonusStat.AttackRoll, CFBonusType.Status, 1, AnthemSeconds);
        _bonuses.AddBonus(ally, CFBonusStat.Damage, CFBonusType.Status, 1, AnthemSeconds);
        _bonuses.AddBonus(ally, CFBonusStat.Saves, CFBonusType.Status, 1, AnthemSeconds, trait: "Fear");
    }

    /// <summary>
    /// The item bonus a maneuver gets from the held weapon: its potency, when it has the matching trait
    /// (trip, shove, grapple, disarm). Fists have none.
    /// </summary>
    public int ManeuverBonus(EntityUid user, Func<CFWeaponTraits, bool> hasTrait)
    {
        if (!_strikes.TryMeleeWeapon(user, out var weapon, out var weaponUid) || weaponUid == user)
            return 0;

        return hasTrait(weapon.Traits) ? weapon.Potency : 0;
    }
}
