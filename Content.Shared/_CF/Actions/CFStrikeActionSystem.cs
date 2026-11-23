using Content.Shared.Actions.Events;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;

namespace Content.Shared.CF.Actions;

/// <summary>Runs actions that are a Strike with something extra. See <see cref="CFStrikeActionComponent"/>.</summary>
public sealed partial class CFStrikeActionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFTauntSystem _taunts = default!;
    [Dependency] private CFInventorSystem _inventor = default!;
    [Dependency] private CFReloadSystem _reload = default!;
    [Dependency] private CFGunslingerSystem _gunslinger = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFActivitySystem _activities = default!;
    [Dependency] private CFActionPoolSystem _pool = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private Content.Shared.Inventory.InventorySystem _inventory = default!;
    [Dependency] private Content.Shared.Throwing.ThrowingSystem _throwing = default!;
    [Dependency] private Content.Shared.CF.Factions.CFFactionSystem _factions = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    /// <summary>
    /// A weapon held in both hands for one attack. One with the two-hand trait uses that die, and any other goes up one step.
    /// </summary>
    public static CFStrikeWeaponStats WithTwoHandedGrip(CFStrikeWeaponStats weapon)
    {
        var die = weapon.Traits.TwoHand > 0 ? Math.Max(weapon.DamageDie, weapon.Traits.TwoHand) : Math.Min(12, weapon.DamageDie + 2);
        return weapon with { DamageDie = die };
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFStrikeActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Target is { } target && WhyNot(args.User, target, ent.Comp) is { } reason)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString(reason);
        }
    }

    [SubscribeLocalEvent]
    private void OnPerform(Entity<CFStrikeActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        // Not handled means the action costs nothing, which is right for one that could not be used.
        if (WhyNot(args.Performer, args.Target, ent.Comp) is { } reason)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString(reason), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (_net.IsServer)
            Strike(ent, args.Performer, args.Target);
    }

    /// <summary>The message for why this cannot be used on that target, or null if it can.</summary>
    private string? WhyNot(EntityUid user, EntityUid target, CFStrikeActionComponent action)
    {
        if (action.FocusPoints > 0
            && (!TryComp(user, out Content.Shared.CF.Character.CFSpellbookComponent? focus)
                || Content.Shared.CF.Spells.CFFocusSystem.Remaining(focus) < action.FocusPoints))
        {
            return "cf-spell-no-focus";
        }

        if (action.PreyOnly && CompOrNull<CFCombatOptionsComponent>(user)?.Prey != target)
            return "cf-strike-action-needs-prey";

        if (action.ProneTarget && !_conditions.IsProne(target))
            return "cf-strike-action-needs-prone";

        if (action.Finisher && CompOrNull<CFCombatOptionsComponent>(user)?.Panache != true)
            return "cf-strike-action-needs-panache";

        if (action.Unstable && !_inventor.CanUseUnstable(user))
            return "cf-inventor-unstable-cannot";

        if (action.InnovationWeapon
            && (!_hands.TryGetActiveItem(user, out var innovation) || !HasComp<CFInnovationComponent>(innovation.Value) || !HasComp<CFWeaponComponent>(innovation.Value)))
        {
            return "cf-strike-action-needs-innovation";
        }

        if (action.WithConstruct && _inventor.ConstructInReach(user) == null)
            return "cf-strike-action-needs-construct";

        if (action.DifferentTarget && CompOrNull<CFActionPoolComponent>(user)?.LastStrikeTarget == target)
            return "cf-strike-action-needs-another";

        if (action.NeedsIgnoredTaunt && !_taunts.HasIgnored(target, user))
            return "cf-strike-action-needs-ignored-taunt";

        if (action.Ranged)
        {
            if (!TryHeldRanged(user, out _, out var bow))
                return "cf-strike-action-needs-ranged";

            if (action.Groups.Count > 0 && (!TryComp(bow, out CFWeaponComponent? shooter) || !action.Groups.Contains(shooter.Group)))
                return "cf-strike-action-needs-group";

            // Risky Reload clears a jam and loads as part of itself.
            if (!action.ReloadsFirst)
            {
                if (_gunslinger.IsJammed(bow))
                    return "cf-gun-jammed";

                if (!Loaded(bow))
                    return "cf-strike-action-needs-ammo";
            }

            if (action.TwoWeapons && OtherShooter(user, bow) == null)
                return "cf-strike-action-needs-two-guns";

            if (action.UsesBomb && CarriedBomb(user) == null)
                return "cf-strike-action-needs-bomb";

            var feet = (_transform.GetWorldPosition(user) - _transform.GetWorldPosition(target)).Length();
            return _interaction.InRangeUnobstructed(user, target, feet + 1f) ? null : "cf-strike-action-no-line";
        }

        if (action.DrawsWeapon && !HoldsWeapon(user) && (HolsteredWeapon(user) == null || _hands.CountFreeHands(user) == 0))
            return "cf-strike-action-needs-holstered";

        if (action.EndsRage && CompOrNull<CFCombatOptionsComponent>(user)?.Raging != true)
            return "cf-strike-action-needs-rage";

        if (action.NeedsSecond && FindSecond(user, target, action) == null)
            return "cf-strike-action-needs-second";

        if (action.Press && CompOrNull<CFActionPoolComponent>(user)?.MapAttacks is null or 0)
            return "cf-strike-action-needs-press";

        if (action.SameTarget
            && (CompOrNull<CFActionPoolComponent>(user) is not { MapAttacks: > 0 } pool || pool.LastStrikeTarget != target))
        {
            return "cf-strike-action-needs-same";
        }

        if (action.NeedsMorph && !Content.Shared.CF.Spells.CFMorphs.IsMorphed(EntityManager, user, needsAttack: true))
            return "cf-strike-action-needs-morph";

        if (action.UnarmedTypes.Count > 0)
        {
            var type = _strikes.UnarmedStats(user).DamageType;
            if (!action.UnarmedTypes.Exists(wanted => string.Equals(wanted, type, StringComparison.OrdinalIgnoreCase)))
                return "cf-strike-action-needs-unarmed-type";
        }

        if (action.NeedsFreeHand && _hands.CountFreeHands(user) == 0)
            return "cf-strike-action-needs-hand";

        if (action.TwoHandedWeapon
            && (!_hands.TryGetActiveItem(user, out var big)
                || !HasComp<CFMeleeWeaponComponent>(big.Value)
                || CompOrNull<CFWeaponComponent>(big.Value)?.Hands != CFWeaponHands.Two))
        {
            return "cf-strike-action-needs-two-handed";
        }

        if (action.Groups.Count > 0
            && (!_hands.TryGetActiveItem(user, out var grouped)
                || !TryComp(grouped.Value, out CFWeaponComponent? data)
                || !action.Groups.Contains(data.Group)))
        {
            return "cf-strike-action-needs-group";
        }

        if (action.TwoWeapons
            && (!_hands.TryGetActiveItem(user, out var main)
                || !HasComp<CFMeleeWeaponComponent>(main.Value)
                || !_strikes.TryOffHandWeapon(user, main.Value, out _, out _)))
        {
            return "cf-strike-action-needs-two-weapons";
        }

        if (action.TwoHandedGrip
            && (!_hands.TryGetActiveItem(user, out var held)
                || !HasComp<CFMeleeWeaponComponent>(held.Value)
                || CompOrNull<CFWeaponComponent>(held.Value)?.Hands == CFWeaponHands.Two
                || _hands.CountFreeHands(user) == 0))
        {
            return "cf-strike-action-needs-free-hand";
        }

        return null;
    }

    private void Strike(Entity<CFStrikeActionComponent> ent, EntityUid user, EntityUid target)
    {
        var action = ent.Comp;
        void Resolved(CFDegree degree, int damage) => RunEffects(ent, user, target, degree, damage);

        if (action.FocusPoints > 0 && TryComp(user, out Content.Shared.CF.Character.CFSpellbookComponent? book))
        {
            book.FocusSpent += action.FocusPoints;
            Dirty(user, book);
        }

        if (action.RaisesShield && _shields.TryGetUsableShield(user, out var shield))
            _shields.SetRaised(shield, true);

        Move(user, target, action);
        if (action.TargetOffGuard)
            _conditions.SetOffGuardTo(target, user, 1f);

        var drawnRanged = false;
        if (action.DrawsWeapon)
        {
            if (!HoldsWeapon(user) && HolsteredWeapon(user) is { } worn)
            {
                _inventory.TryUnequip(user, worn.Slot, silent: true);
                _hands.TryPickupAnyHand(user, worn.Item, animate: false);
            }

            drawnRanged = TryHeldRanged(user, out _, out _);
        }

        if (action.Ranged || drawnRanged)
        {
            if (!TryHeldRanged(user, out var shot, out var bow))
                return;

            if (action.ReloadsFirst && (!_reload.TryReload(user, prepaid: false) || !Loaded(bow)))
                return;

            string? bombType = null;
            if (action.UsesBomb && CarriedBomb(user) is { } bomb)
            {
                bombType = CompOrNull<CFWeaponComponent>(bomb)?.DamageType ?? shot.DamageType;
                shot = shot with { DamageType = bombType };
                Del(bomb);
            }

            // A shot that misses can jam the gun, and one soaked in a bomb burns whoever it hits, or the shooter.
            void Shot(CFDegree degree, int damage)
            {
                var landed = degree is CFDegree.Success or CFDegree.CriticalSuccess;
                if (bombType != null)
                {
                    if (landed && damage > 0)
                        _bonuses.AddPersistent(target, bombType, 1, 6, 0, user);
                    else if (!landed)
                        _effects.DealDamage(user, user, _effects.Roll(1, 6, 0), bombType, critical: false);
                }

                if (action.Misfires && !landed)
                    _gunslinger.Jam(bow, user);

                Resolved(degree, damage);
            }

            var shotMap = _pool.MapPenalty(user, shot.Agile);
            SpendArrow(user, bow);
            var landedFirst = _strikes.PerformStrike(user, target, shot, bow, melee: false, attackPenalty: action.AttackBonus, resolved: Shot, ignoreConcealed: action.IgnoresConcealed);

            // Paired Shots: the weapon in the other hand fires at the same target and the same penalty.
            if (action.TwoWeapons && OtherShooter(user, bow) is { } paired && _weapons.TryStats(paired, out var pairedShot))
            {
                SpendArrow(user, paired);
                _strikes.PerformStrike(user, target, pairedShot, paired, melee: false, attackPenalty: action.AttackBonus, fixedMap: shotMap, skipPrecision: landedFirst, resolved: Resolved);
                return;
            }

            // A second shot needs a second arrow nocked, at the same target or at someone else.
            var next = action.Twice ? target : FindSecond(user, target, action);
            if (next is { } aim && _reload.TryReload(user, prepaid: true) && TryHeldRanged(user, out shot, out bow))
            {
                void Again(CFDegree degree, int damage) => RunEffects(ent, user, aim, degree, damage);
                SpendArrow(user, bow);
                _strikes.PerformStrike(user, aim, shot, bow, melee: false, attackPenalty: action.AttackBonus, fixedMap: action.Twice ? null : shotMap, resolved: Again);
                _reload.TryReload(user, prepaid: true);
            }

            return;
        }

        CFStrikeWeaponStats stats;
        EntityUid weapon;
        if (action.UnarmedOnly && action.Reloads && OffHandBlade(user) is { } blade && _weapons.TryStats(blade, out var bladeStats))
        {
            // Reloading Strike: the melee weapon in the other hand, when there is one.
            stats = bladeStats;
            weapon = blade;
        }
        else if (action.UnarmedOnly)
        {
            stats = _strikes.UnarmedStats(user);
            weapon = user;
        }
        else if (!_strikes.TryMeleeWeapon(user, out stats, out weapon))
        {
            return;
        }

        if (action.TwoHandedGrip)
            stats = WithTwoHandedGrip(stats);

        // Spent first, so a finisher that drops its target can earn panache back.
        if (action.Finisher && TryComp(user, out CFCombatOptionsComponent? options))
        {
            options.Panache = false;
            Dirty(user, options);
        }

        var map = _pool.MapPenalty(user, stats.Agile);
        var second = FindSecond(user, target, action, stats);
        var fury = action.EndsRage ? _activities.RageRoundsLeft(user) : 0;
        var hit = _strikes.PerformStrike(user, target, stats, weapon, melee: true, attackPenalty: action.AttackBonus, extraDice: action.ExtraDice, bonusDamage: fury + action.BonusPerDie * (stats.DamageDice + action.ExtraDice), finisher: action.Finisher, resolved: Resolved);
        if (action.CountsAsTwo)
            _pool.CountAttack(user);

        if (action.EndsRage)
        {
            _activities.SetRaging(user, false);
            _activities.Fatigue(user);
        }

        if (second is { } other)
        {
            void Other(CFDegree degree, int damage) => RunEffects(ent, user, other, degree, damage);
            _strikes.PerformStrike(user, other, stats, weapon, melee: true, attackPenalty: action.AttackBonus, extraDice: action.ExtraDice, bonusDamage: action.BonusPerDie * (stats.DamageDice + action.ExtraDice), finisher: action.Finisher, countMap: !action.SecondIsFree, fixedMap: map, resolved: Other);
        }

        if (action.TwoWeapons && _strikes.TryOffHandWeapon(user, weapon, out var off, out var offHand))
            _strikes.PerformStrike(user, target, off, offHand, melee: true, attackPenalty: action.AttackBonus, skipPrecision: hit, resolved: Resolved);

        if (action.Twice)
            _strikes.PerformStrike(user, target, stats, weapon, melee: true, attackPenalty: action.AttackBonus - (stats.Agile ? 0 : 2), skipPrecision: hit, resolved: Resolved);

        if (action.WithConstruct && _inventor.ConstructInReach(user) is { } construct)
        {
            Rush(construct, target, action.ClosesFeet);
            _strikes.PerformStrike(construct, target, _strikes.UnarmedStats(construct), construct, melee: true);
        }

        if (action.Unstable)
            _inventor.RollUnstable(user);

        if (action.TauntsTarget)
            _taunts.Taunt(user, target);

        if (action.Reloads)
            _reload.TryReload(user, prepaid: true, needsHand: false);

        if (action.RechargesSpellstrike)
            _activities.RechargeSpellstrike(user);
    }

    private bool HoldsWeapon(EntityUid user)
    {
        return _hands.TryGetActiveItem(user, out var held) && HasComp<CFWeaponComponent>(held.Value);
    }

    /// <summary>A weapon worn on the belt, back or neck, and the slot it is in.</summary>
    private (EntityUid Item, string Slot)? HolsteredWeapon(EntityUid user)
    {
        foreach (var slot in Content.Shared.CF.Items.CFEncumbranceSystem.HolsterSlots)
        {
            if (_inventory.TryGetSlotEntity(user, slot, out var item) && HasComp<CFWeaponComponent>(item.Value) && !HasComp<CFShieldComponent>(item.Value))
                return (item.Value, slot);
        }

        return null;
    }

    /// <summary>How fast the user covers the ground in a rush or a hop back. Stock's jump speed.</summary>
    private const float RushSpeed = 10f;

    /// <summary>The rush toward the target, or the hop away from it, that comes before the Strike.</summary>
    private void Move(EntityUid user, EntityUid target, CFStrikeActionComponent action)
    {
        if (action.ClosesFeet <= 0 && action.StepsBackFeet <= 0)
            return;

        if (action.StepsBackFeet > 0)
        {
            var from = _transform.GetWorldPosition(user);
            var gap = _transform.GetWorldPosition(target) - from;
            var distance = gap.Length();
            if (distance < 0.01f)
                return;

            var direction = gap / distance;
            _throwing.TryThrow(user, Transform(user).Coordinates.Offset(direction * (-action.StepsBackFeet / CFMelee.FeetPerTile)), RushSpeed, doSpin: false);
            return;
        }

        Rush(user, target, action.ClosesFeet);
    }

    /// <summary>Rushes a creature toward the target, up to this many feet, stopping one tile short of it.</summary>
    private void Rush(EntityUid mover, EntityUid target, int feet)
    {
        if (feet <= 0)
            return;

        var from = _transform.GetWorldPosition(mover);
        var gap = _transform.GetWorldPosition(target) - from;
        var distance = gap.Length();
        if (distance < 0.01f)
            return;

        var direction = gap / distance;
        var tiles = Math.Min(feet / CFMelee.FeetPerTile, distance - 1f);
        if (tiles <= 0.1f)
            return;

        _throwing.TryThrow(mover, Transform(mover).Coordinates.Offset(direction * tiles), RushSpeed, doSpin: false);
    }

    /// <summary>The enemy an action's second Strike goes at, or null when there is none to be had.</summary>
    private EntityUid? FindSecond(EntityUid user, EntityUid target, CFStrikeActionComponent action, CFStrikeWeaponStats? weapon = null)
    {
        if (action.Second == CFSecondStrike.None)
            return null;

        var from = _transform.GetWorldPosition(user);
        var at = _transform.GetWorldPosition(target);
        var reachFeet = Math.Max(CFFlanking.DefaultReachFeet, weapon?.Traits.Reach ?? 0);
        var ranged = action.Second == CFSecondStrike.OtherInRange;
        // Half a tile past the reach circle, so a creature standing diagonally is still in reach.
        var search = ranged ? 60f / CFMelee.FeetPerTile : CFFlanking.ReachInUnits(reachFeet) + 2f;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (uid, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(user).Coordinates, search))
        {
            if (uid == user || uid == target || !_mobState.IsAlive(uid) || !_factions.CanHarm(user, uid))
                continue;

            var position = _transform.GetWorldPosition(uid);
            var distance = (position - from).Length();
            if (ranged)
            {
                if (!_interaction.InRangeUnobstructed(user, uid, distance + 1f))
                    continue;
            }
            else if (distance > CFFlanking.ReachInUnits(reachFeet) + 0.5f)
            {
                // Impaling Finisher reaches the creature behind the one in reach.
                if (action.Second != CFSecondStrike.BehindTarget)
                    continue;
            }

            if (action.Second is CFSecondStrike.BesideTarget or CFSecondStrike.BehindTarget && (position - at).Length() > 1.5f)
                continue;

            if (action.Second == CFSecondStrike.BehindTarget)
            {
                var forward = at - from;
                var beyond = position - at;
                if (forward.LengthSquared() < 0.01f
                    || beyond.LengthSquared() < 0.01f
                    || System.Numerics.Vector2.Dot(System.Numerics.Vector2.Normalize(forward), System.Numerics.Vector2.Normalize(beyond)) < 0.7f)
                {
                    continue;
                }
            }

            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    private void RunEffects(Entity<CFStrikeActionComponent> ent, EntityUid user, EntityUid target, CFDegree degree, int damage)
    {
        List<CFEffect> effects;
        if (degree == CFDegree.Failure)
        {
            if (ent.Comp.OnFailure.Count == 0)
                return;

            effects = ent.Comp.OnFailure;
        }
        else if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess) || ent.Comp.EffectsNeedDamage && damage <= 0)
        {
            return;
        }
        else
        {
            effects = degree == CFDegree.CriticalSuccess && ent.Comp.OnCritical.Count > 0 ? ent.Comp.OnCritical : ent.Comp.OnHit;
        }

        _effects.Run(effects, new CFEffectArgs
        {
            User = user,
            Source = ent,
            Target = target,
            Position = Transform(target).Coordinates,
            Degree = degree,
            Defense = CFDefenseType.ArmorClass,
        });
    }

    private bool TryHeldRanged(EntityUid user, out CFStrikeWeaponStats stats, out EntityUid weapon)
    {
        stats = default;
        weapon = default;
        if (!_hands.TryGetActiveItem(user, out var held) || !_weapons.TryStats(held.Value, out stats) || !stats.Ranged)
            return false;

        weapon = held.Value;
        return true;
    }

    /// <summary>A one-handed melee weapon held beside a ranged one.</summary>
    private EntityUid? OffHandBlade(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFMeleeWeaponComponent>(held)
                && !HasComp<CFRangedWeaponComponent>(held)
                && !HasComp<CFShieldComponent>(held)
                && CompOrNull<CFWeaponComponent>(held)?.Hands == CFWeaponHands.One)
            {
                return held;
            }
        }

        return null;
    }

    private bool Loaded(EntityUid bow)
    {
        var count = new GetAmmoCountEvent();
        RaiseLocalEvent(bow, ref count);
        return count.Count > 0;
    }

    /// <summary>A loaded one-handed ranged weapon in another hand than <paramref name="bow"/>, when that is one-handed too.</summary>
    private EntityUid? OtherShooter(EntityUid user, EntityUid bow)
    {
        if (CompOrNull<CFWeaponComponent>(bow)?.Hands != CFWeaponHands.One)
            return null;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held != bow
                && HasComp<CFRangedWeaponComponent>(held)
                && CompOrNull<CFWeaponComponent>(held)?.Hands == CFWeaponHands.One
                && !_gunslinger.IsJammed(held)
                && Loaded(held))
            {
                return held;
            }
        }

        return null;
    }

    /// <summary>An alchemical bomb in a hand or worn.</summary>
    private EntityUid? CarriedBomb(EntityUid user)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(user))
        {
            if (HasComp<Content.Shared.CF.Alchemy.CFBombComponent>(item))
                return item;
        }

        return null;
    }

    private void SpendArrow(EntityUid user, EntityUid bow)
    {
        var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();
        RaiseLocalEvent(bow, new TakeAmmoEvent(1, ammo, Transform(user).Coordinates, user));
        foreach (var (arrow, _) in ammo)
        {
            if (arrow is { } entity)
                Del(entity);
        }
    }
}
