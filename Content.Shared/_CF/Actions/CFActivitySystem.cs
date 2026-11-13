using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// The activities still on the action wedge. Most actions use stock inputs instead: see <see cref="CFManeuverSystem"/>.
/// Class features and chosen feats are the same kind of action, granted at spawn.
/// </summary>
public sealed partial class CFActivitySystem : EntitySystem
{
    /// <summary>
    /// How long Courageous Anthem lasts from the action. Printed as 1 round, which is too short with no turns, so it is
    /// a real minute. Playing a held instrument keeps it going for as long as the music lasts instead.
    /// </summary>
    public static readonly TimeSpan AnthemLasts = TimeSpan.FromMinutes(1);

    /// <summary>Stock's jump speed. A leap lands as soon as it has covered the distance.</summary>
    private const float LeapThrowSpeed = 10f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFTrapLayerSystem _traps = default!;
    [Dependency] private CFSpellSystem _spells = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private CFTemporaryHpSystem _tempHp = default!;
    [Dependency] private CFAuraSystem _auras = default!;

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
            case CFActivityKind.Spellstrike when CompOrNull<CFCombatOptionsComponent>(args.User)?.SpellstrikeSpent == true:
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-spellstrike-spent");
                break;
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
            case CFActivityKind.SpellstrikeRecharge:
                RechargeSpellstrike(args.Performer);
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
            case CFActivityKind.MagusAnalysis:
                _maneuvers.TryRecall(args.Performer, args.Target);
                RechargeSpellstrike(args.Performer);
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

        SetRaging(user, !opt.Raging);
        _popup.PopupEntity(Loc.GetString(opt.Raging ? "cf-act-rage-start" : "cf-act-rage-end"), user, user);
    }

    public const string RageSource = "rage";
    private static readonly TimeSpan RageTempHpWait = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Starts or ends a Rage. Starting gives temporary Hit Points equal to level plus Constitution modifier, but only once a
    /// minute unless the creature has Second Wind. They last until the Rage ends.
    /// </summary>
    public void SetRaging(EntityUid user, bool raging)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        if (opt.Raging == raging)
            return;

        opt.Raging = raging;
        if (raging)
        {
            var now = _timing.CurTime;
            if (opt.SecondWind || now >= opt.RageTempHpReadyAt)
            {
                var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
                var constitution = CompOrNull<CFAbilityScoreComponent>(user)?.GetModifier(CFAbility.Constitution) ?? 0;
                _tempHp.Grant(user, level + constitution, 0f, RageSource);
                opt.RageTempHpReadyAt = now + RageTempHpWait;
            }
        }
        else
        {
            _tempHp.Clear(user, RageSource);
        }

        Dirty(user, opt);
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
        if (TryComp(user, out CFCombatOptionsComponent? options))
        {
            options.SpellstrikeSpent = true;
            Dirty(user, options);
        }
    }

    /// <summary>Makes Spellstrike ready again.</summary>
    public void RechargeSpellstrike(EntityUid user)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? options) || !options.SpellstrikeSpent)
            return;

        options.SpellstrikeSpent = false;
        Dirty(user, options);
        _popup.PopupEntity(Loc.GetString("cf-act-spellstrike-ready"), user, user);
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

    /// <summary>
    /// Courageous Anthem for <see cref="AnthemLasts"/>: +1 status to attack rolls, damage rolls, and saves against fear
    /// for the Bard and allies within 60 feet, as an aura that follows the Bard.
    /// </summary>
    public void Anthem(EntityUid user)
    {
        Anthem(user, AnthemLasts);
    }

    public void Anthem(EntityUid user, TimeSpan lasts)
    {
        var started = !_auras.AnthemActive(user);
        _auras.Anthem(user, lasts);
        if (started)
            _popup.PopupEntity(Loc.GetString("cf-act-anthem"), user, user);
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
