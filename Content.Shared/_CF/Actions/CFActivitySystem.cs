using System.Numerics;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Systems;
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
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private CFTemporaryHpSystem _tempHp = default!;
    [Dependency] private CFAuraSystem _auras = default!;
    [Dependency] private Robust.Shared.Prototypes.IPrototypeManager _prototypes = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private Content.Shared.CF.Character.CFCharacterFeatureSystem _features = default!;

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
            case CFActivityKind.Rage
                when CompOrNull<CFCombatOptionsComponent>(args.User)?.Raging != true
                     && _conditions.Value(args.User, CFConditionKind.Fatigued) > 0:
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-rage-fatigued");
                break;
            case CFActivityKind.Leap when !_maneuvers.CanUse(args.User, CFBasicAction.Leap):
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-leap-cannot");
                break;
            case CFActivityKind.LeftHandBlood when !HoldsWeapon(args.User):
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-needs-weapon");
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
            case CFActivityKind.TailToxin:
                Envenom(args.Performer, dice: 0, die: 0, unarmedOnly: false);
                break;
            case CFActivityKind.EnvenomFangs:
                Envenom(args.Performer, dice: 1, die: 6, unarmedOnly: true);
                break;
            case CFActivityKind.LeftHandBlood:
                _effects.DealDamage(args.Performer, args.Performer, 1, "Slashing", critical: false);
                Envenom(args.Performer, dice: 1, die: 4, unarmedOnly: false, anyWeapon: true);
                break;
            case CFActivityKind.SparkFist:
                var fist = EnsureComp<CFCombatOptionsComponent>(args.Performer);
                fist.SparkFistUntil = _timing.CurTime + TimeSpan.FromSeconds(6);
                Dirty(args.Performer, fist);
                _popup.PopupEntity(Loc.GetString("cf-act-spark-fist"), args.Performer, args.Performer);
                break;
        }
    }

    /// <summary>
    /// Venom on the creature's weapon or fangs for 6 seconds. Dice of 0 means persistent poison equal to the level.
    /// <paramref name="anyWeapon"/> is poison on a held weapon, whatever kind of damage it deals.
    /// </summary>
    private void Envenom(EntityUid user, int dice, int die, bool unarmedOnly, bool anyWeapon = false)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(user);
        options.TailToxinUntil = _timing.CurTime + TimeSpan.FromSeconds(6);
        options.ToxinDice = dice;
        options.ToxinDie = die;
        options.ToxinUnarmedOnly = unarmedOnly;
        options.ToxinAnyWeapon = anyWeapon;
        Dirty(user, options);
        _popup.PopupEntity(Loc.GetString("cf-act-tail-toxin"), user, user);
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
            case CFActivityKind.Demoralize:
                _conditions.TryDemoralize(args.Performer, args.Target);
                break;
            case CFActivityKind.Feint:
                _conditions.TryFeint(args.Performer, args.Target);
                break;
            case CFActivityKind.OverextendingFeint:
                _conditions.TryFeint(args.Performer, args.Target, overextending: true);
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

    private bool HoldsWeapon(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFWeaponComponent>(held))
                return true;
        }

        return false;
    }

    /// <summary>How long fatigue lasts: a minute of real time. The rules end it with a 10 minute rest.</summary>
    public const float FatigueSeconds = 60f;

    /// <summary>Fatigued for <see cref="FatigueSeconds"/>, as after Second Wind or Furious Finish. A fatigued creature cannot Rage.</summary>
    public void Fatigue(EntityUid user)
    {
        _conditions.ApplyCondition(user, CFConditionKind.Fatigued, 1, FatigueSeconds, user, 0);
    }

    /// <summary>How many rounds of the creature's Rage are left, for Furious Finish. 0 when it is not raging.</summary>
    public int RageRoundsLeft(EntityUid user)
    {
        if (!TryComp(user, out CFRagingComponent? raging))
            return 0;

        return Math.Clamp((int) Math.Ceiling((raging.EndsAt - _timing.CurTime).TotalSeconds / 6.0), 0, 10);
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

    /// <summary>How long a Rage lasts. The player may also end it sooner, which stands in for the encounter ending.</summary>
    public static readonly TimeSpan RageLasts = TimeSpan.FromMinutes(1);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFRagingComponent>();
        while (query.MoveNext(out var uid, out var raging))
        {
            if (now < raging.EndsAt && _mobState.IsAlive(uid))
                continue;

            SetRaging(uid, false);
            _popup.PopupEntity(Loc.GetString("cf-act-rage-end"), uid, uid);
        }
    }

    /// <summary>
    /// Starts or ends a Rage. Starting gives temporary Hit Points equal to level plus Constitution modifier, but only once a
    /// minute unless the creature has Second Wind. They last until the Rage ends, which it does after
    /// <see cref="RageLasts"/> or when the creature drops.
    /// </summary>
    /// <summary>Acute Vision and Acute Scent: senses a Rage brings and takes away again. One the creature has anyway is left alone.</summary>
    private void RageSenses(EntityUid user, CFCombatOptionsComponent opt, bool raging)
    {
        if (!_net.IsServer)
            return;

        if (raging)
        {
            if (opt.RageDarkvision && CompOrNull<CFSensesComponent>(user)?.Darkvision != true)
            {
                _features.SetRageVision(user, true);
                opt.RageGaveVision = true;
            }

            if (opt.RageScent && !HasComp<Content.Shared.CF.Scent.CFSmellerComponent>(user))
            {
                EnsureComp<Content.Shared.CF.Scent.CFSmellerComponent>(user);
                opt.RageGaveScent = true;
            }

            return;
        }

        if (opt.RageGaveVision)
            _features.SetRageVision(user, false);

        if (opt.RageGaveScent)
            RemCompDeferred<Content.Shared.CF.Scent.CFSmellerComponent>(user);

        opt.RageGaveVision = false;
        opt.RageGaveScent = false;
    }

    public void SetRaging(EntityUid user, bool raging)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        if (opt.Raging == raging)
            return;

        opt.Raging = raging;
        if (raging)
        {
            var now = _timing.CurTime;
            EnsureComp<CFRagingComponent>(user).EndsAt = now + RageLasts;
            if (opt.SecondWind || now >= opt.RageTempHpReadyAt)
            {
                // Second Wind takes the temporary Hit Points early, and pays for it with fatigue when the Rage ends.
                EnsureComp<CFRagingComponent>(user).Tiring = now < opt.RageTempHpReadyAt;
                var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
                var constitution = CompOrNull<CFAbilityScoreComponent>(user)?.GetModifier(CFAbility.Constitution) ?? 0;
                _tempHp.Grant(user, level + constitution, 0f, RageSource);
                opt.RageTempHpReadyAt = now + RageTempHpWait;

                // Superstition: the same amount in real Hit Points, once per 10 minutes.
                if (opt.RageHeals && now >= opt.RageHealReadyAt)
                {
                    _dying.RestoreHitPoints(user, level + constitution);
                    opt.RageHealReadyAt = now + TimeSpan.FromMinutes(10);
                }
            }

            RageSenses(user, opt, true);
        }
        else
        {
            RageSenses(user, opt, false);
            if (CompOrNull<CFRagingComponent>(user)?.Tiring == true)
                Fatigue(user);

            RemCompDeferred<CFRagingComponent>(user);
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
            if (opt.MonsterHunter)
                _maneuvers.TryRecall(user, target, free: true);
        }
        else
        {
            opt.Exploited = target;
            _conditions.SetOffGuardTo(target, user, 60f);
            _popup.PopupEntity(Loc.GetString("cf-act-exploit"), user, user);
            if (opt.EsotericWarden)
            {
                _bonuses.AddBonus(user, CFBonusStat.ArmorClass, CFBonusType.Status, 1, 6f);
                _bonuses.AddBonus(user, CFBonusStat.Saves, CFBonusType.Status, 1, 6f);
            }

            if (opt.DiverseLore)
                _maneuvers.TryRecall(user, target, free: true);
        }

        Dirty(user, opt);
    }

    public void Stratagem(EntityUid user, EntityUid target)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        opt.StratagemRoll = _random.Next(1, 21);
        opt.StratagemTarget = target;
        Dirty(user, opt);
        if (opt.KnownWeaknesses)
            _maneuvers.TryRecall(user, target, free: true);
        _popup.PopupEntity(Loc.GetString("cf-act-stratagem", ("roll", opt.StratagemRoll)), user, user);
    }

    private void Flurry(EntityUid user, EntityUid target)
    {
        var stats = _strikes.UnarmedStats(user);
        var hurt = false;
        void Resolved(CFDegree degree, int damage) => hurt |= degree.IsSuccess() && damage > 0;
        _strikes.PerformStrike(user, target, stats, user, melee: true, resolved: Resolved);
        _strikes.PerformStrike(user, target, stats, user, melee: true, resolved: Resolved);

        // Stunning Blows: both Strikes went at one creature, so one that hurt it may stun.
        if (!hurt || CompOrNull<CFCombatOptionsComponent>(user)?.StunningBlows != true)
            return;

        var dc = CFCasting.ClassDc(_prototypes, CompOrNull<CFCombatantComponent>(user), CompOrNull<CFAbilityScoreComponent>(user)).Dc;
        var save = _conditions.RollDefense(target, CFDefenseType.Fortitude, dc, out _);
        if (!save.IsSuccess())
            _conditions.ApplyCondition(target, CFConditionKind.Stunned, save == CFDegree.CriticalFailure ? 3 : 1, 0f, user, 0);
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

        if (CompOrNull<CFCombatOptionsComponent>(user)?.DancingLeaf == true)
            tiles++;

        LeapBy(user, tiles);
    }

    /// <summary>Jumps the way the creature faces, this many tiles.</summary>
    public void LeapBy(EntityUid user, float tiles)
    {
        var xform = Transform(user);
        var landing = xform.Coordinates.Offset(xform.LocalRotation.ToWorldVec() * tiles);
        EnsureComp<CFLeapingComponent>(user);
        _throwing.TryThrow(user, landing, LeapThrowSpeed, doSpin: false);
    }

    /// <summary>
    /// A Leap covers its distance and no more: the creature stops where it lands, instead of sliding on at the
    /// speed of the throw. Standing still there, whatever is under it is what it landed on, an opening included.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLeapEnded(Entity<CFLeapingComponent> ent, ref StopThrowEvent args)
    {
        _physics.SetLinearVelocity(ent, Vector2.Zero);
        RemComp<CFLeapingComponent>(ent);
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
