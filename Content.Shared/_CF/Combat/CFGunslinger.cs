using Content.Shared.CF.Actions;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Locks;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>What a Gunslinger's way does as a fight begins. <see cref="CFGunslingerSystem"/> runs it.</summary>
public enum CFInitialDeed : byte
{
    None,

    /// <summary>Drifter: draws a one-handed ranged weapon and a one-handed melee weapon.</summary>
    IntoTheFray,

    /// <summary>Pistolero: draws a one-handed firearm or crossbow, and can Step for free for a moment.</summary>
    TenPaces,

    /// <summary>Sniper: draws a firearm or crossbow, and the first Strike with one that hits deals 1d6 more.</summary>
    OneShotOneKill,

    /// <summary>Vanguard: draws a firearm or crossbow and holds it as a guard, +1 AC for 6 seconds.</summary>
    LivingFortification,

    /// <summary>Spellshot: draws a firearm or crossbow, and the first three Strikes with one deal 1 fire damage per weapon die.</summary>
    EnergyShot,
}

/// <summary>Sets what a Gunslinger does as a fight begins.</summary>
public sealed partial class CFGrantInitialDeed : CFGrant
{
    [DataField(required: true)]
    public CFInitialDeed Deed;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var options = entities.EnsureComponent<CFCombatOptionsComponent>(args.Mob);
        options.InitialDeed = Deed;
        entities.Dirty(args.Mob, options);
    }
}

public enum CFGunActionKind : byte
{
    /// <summary>A Feint at the weapon's first range increment, good against ranged attacks too. Needs a loaded one-handed ranged weapon.</summary>
    PistolTwirl,

    /// <summary>A Demoralize by firing into the air. Spends the round of a loaded firearm.</summary>
    WarningShot,

    /// <summary>An attack roll against a lock's DC. Spends the round of a loaded firearm.</summary>
    BlastLock,

    /// <summary>A Leap 10 feet longer, on the kick of a loaded firearm. Spends the round.</summary>
    BlackPowderBoost,
}

/// <summary>An action a held firearm or crossbow does that is not a Strike. <see cref="CFGunslingerSystem"/> runs it.</summary>
[RegisterComponent]
public sealed partial class CFGunActionComponent : Component
{
    [DataField(required: true)]
    public CFGunActionKind Kind;
}

/// <summary>
/// Misfires, and what a Gunslinger does that a Strike action cannot say. A misfired firearm is jammed: it cannot be loaded
/// or fired until 1 action clears it, which pulling the trigger or reloading does. Nothing misfires on its own here, since
/// there is no daily cleaning: only Risky Reload and Alchemical Shot jam a gun.
/// </summary>
public sealed partial class CFGunslingerSystem : EntitySystem
{
    /// <summary>How far a creature can be for Sword and Pistol to count it as in reach, in tiles.</summary>
    private const float ReachTiles = 1.75f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private CFActionPoolSystem _pool = default!;
    [Dependency] private CFActivitySystem _activities = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFReactionSystem _reactions = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFLockSystem _locks = default!;

    public bool IsJammed(EntityUid gun)
    {
        return CompOrNull<CFRangedWeaponComponent>(gun)?.Jammed == true;
    }

    /// <summary>The firearm misfires and jams. With Instant Backup and a reaction left, its holder drops it and draws another weapon.</summary>
    public void Jam(EntityUid gun, EntityUid user)
    {
        if (!TryComp(gun, out CFRangedWeaponComponent? ranged)
            || ranged.Jammed
            || CompOrNull<CFWeaponComponent>(gun)?.Group != CFWeaponGroup.Firearm)
        {
            return;
        }

        ranged.Jammed = true;
        Dirty(gun, ranged);
        _popup.PopupEntity(Loc.GetString("cf-gun-misfire"), user, PopupType.MediumCaution);

        if (!TryComp(user, out CFCombatOptionsComponent? options)
            || !options.InstantBackup
            || options.ReactionSpent != 0
            || Holstered(user, _ => true) is not { } backup)
        {
            return;
        }

        // A backup has to fit the hand the gun leaves: a two-handed one only replaces a two-handed gun.
        var gunHands = CompOrNull<CFWeaponComponent>(gun)?.Hands ?? CFWeaponHands.One;
        if (CompOrNull<CFWeaponComponent>(backup.Item)?.Hands == CFWeaponHands.Two && gunHands != CFWeaponHands.Two)
            return;

        if (!_hands.TryDrop(user, gun))
            return;

        options.ReactionSpent = CFReactions.Granted;
        Dirty(user, options);
        Draw(user, backup);
        _popup.PopupEntity(Loc.GetString("cf-gun-instant-backup"), user, user);
    }

    /// <summary>Clears a jam for 1 action. True when the weapon is not jammed afterwards.</summary>
    public bool TryClear(EntityUid gun, EntityUid user)
    {
        if (!TryComp(gun, out CFRangedWeaponComponent? ranged) || !ranged.Jammed)
            return true;

        if (!_pool.TrySpendActions(user, 1))
            return false;

        ranged.Jammed = false;
        Dirty(gun, ranged);
        _popup.PopupEntity(Loc.GetString("cf-gun-jam-cleared"), user, user);
        return true;
    }

    /// <summary>Pulling the trigger of a jammed gun clears the jam and does not fire.</summary>
    [SubscribeLocalEvent]
    private void OnShotAttempt(Entity<CFRangedWeaponComponent> ent, ref ShotAttemptedEvent args)
    {
        if (!ent.Comp.Jammed)
            return;

        args.Cancel();
        if (_net.IsServer)
            TryClear(ent, args.User);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CFRangedWeaponComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Jammed)
            args.PushMarkup(Loc.GetString("cf-gun-jammed-examine"));
    }

    /// <summary>What a way's first deed does. Called by <c>CFFightSystem</c> as a fight begins.</summary>
    public void StartFight(Entity<CFCombatOptionsComponent> ent)
    {
        var options = ent.Comp;
        switch (options.InitialDeed)
        {
            case CFInitialDeed.None:
                return;
            case CFInitialDeed.IntoTheFray:
                DrawIf(ent, item => IsGun(item) && OneHanded(item));
                DrawIf(ent, item => HasComp<CFMeleeWeaponComponent>(item) && OneHanded(item), evenIfArmed: true);
                break;
            case CFInitialDeed.TenPaces:
                DrawIf(ent, item => IsGun(item) && OneHanded(item));
                _reactions.CoverSteps(ent, 3f);
                break;
            case CFInitialDeed.OneShotOneKill:
                DrawIf(ent, IsGun);
                options.OneShotReady = true;
                break;
            case CFInitialDeed.LivingFortification:
                DrawIf(ent, IsGun);
                _bonuses.AddBonus(ent, CFBonusStat.ArmorClass, CFBonusType.Circumstance, 1, 6f, source: "cf-living-fortification");
                break;
            case CFInitialDeed.EnergyShot:
                DrawIf(ent, IsGun);
                options.EnergyShots = 3;
                break;
        }

        Dirty(ent);
    }

    /// <summary>Extra damage and openings a Gunslinger gets from a Strike with a firearm or crossbow.</summary>
    [SubscribeLocalEvent]
    private void OnStrikeResolved(Entity<CFRangedWeaponComponent> ent, ref CFStrikeResolvedEvent args)
    {
        if (args.Melee
            || !TryComp(args.User, out CFCombatOptionsComponent? options)
            || !TryComp(ent, out CFWeaponComponent? weapon)
            || weapon.Group is not (CFWeaponGroup.Firearm or CFWeaponGroup.Crossbow))
        {
            return;
        }

        var hit = args.Degree is CFDegree.Success or CFDegree.CriticalSuccess && args.Damage > 0;
        var critical = args.Degree == CFDegree.CriticalSuccess;
        var dice = _weapons.TryStats(ent, out var stats) ? Math.Max(1, stats.DamageDice) : 1;
        var changed = false;

        if (options.EnergyShots > 0)
        {
            options.EnergyShots--;
            changed = true;
            if (hit)
                _effects.DealDamage(args.User, args.Target, critical ? dice * 2 : dice, "Fire", critical: false);
        }

        if (options.OneShotReady && hit)
        {
            options.OneShotReady = false;
            changed = true;
            var extra = _effects.Roll(1, 6, 0);
            _effects.DealDamage(args.User, args.Target, critical ? extra * 2 : extra, stats.DamageType ?? "Piercing", critical: false);
        }

        if (options.CrossbowCrackShot && weapon.Group == CFWeaponGroup.Crossbow && _timing.CurTime >= options.CrackShotAt)
        {
            options.CrackShotAt = _timing.CurTime + TimeSpan.FromSeconds(6);
            changed = true;
            if (hit)
                _effects.DealDamage(args.User, args.Target, critical ? dice * 2 : dice, stats.DamageType ?? "Piercing", critical: false);
        }

        if (options.SwordAndPistol && hit && weapon.Hands == CFWeaponHands.One && InReach(args.User, args.Target))
            _conditions.OffGuardToNextMelee(args.User, args.Target);

        if (changed)
            Dirty(args.User, options);
    }

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFGunActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled || ent.Comp.Kind != CFGunActionKind.BlackPowderBoost)
            return;

        if (!TryLoaded(args.Performer, firearm: true, oneHanded: false, out var gun))
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        Fire(args.Performer, gun);
        _activities.LeapBy(args.Performer, CFActivitySystem.LeapTiles(CompOrNull<CFCombatantComponent>(args.Performer)?.SpeedFeet ?? 0) + 2);
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFGunActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        switch (ent.Comp.Kind)
        {
            case CFGunActionKind.PistolTwirl:
                if (!TryLoaded(user, firearm: false, oneHanded: true, out var twirled) || !InFirstIncrement(user, args.Target, twirled))
                    return;

                args.Handled = true;
                if (_net.IsServer && _conditions.TryFeint(user, args.Target))
                    _conditions.FeintAlsoRanged(user, args.Target);

                break;
            case CFGunActionKind.WarningShot:
                if (!TryLoaded(user, firearm: true, oneHanded: false, out var warned))
                    return;

                args.Handled = true;
                if (!_net.IsServer)
                    return;

                Fire(user, warned);
                _conditions.TryDemoralize(user, args.Target);
                break;
            case CFGunActionKind.BlastLock:
                if (!TryComp(args.Target, out CFLockComponent? locked) || !TryLoaded(user, firearm: true, oneHanded: false, out var blaster))
                    return;

                args.Handled = true;
                if (!_net.IsServer || !_weapons.TryStats(blaster, out var stats))
                    return;

                Fire(user, blaster);
                _locks.TryBlast((args.Target, locked), user, _strikes.RangedAttackModifier(user, args.Target, stats));
                break;
        }
    }

    /// <summary>The loaded weapon in the active hand, with a popup saying why not when there is none.</summary>
    private bool TryLoaded(EntityUid user, bool firearm, bool oneHanded, out EntityUid gun)
    {
        gun = default;
        string? reason = null;
        if (!_hands.TryGetActiveItem(user, out var held)
            || !HasComp<CFRangedWeaponComponent>(held.Value)
            || !TryComp(held.Value, out CFWeaponComponent? weapon)
            || firearm && weapon.Group != CFWeaponGroup.Firearm
            || oneHanded && weapon.Hands != CFWeaponHands.One)
        {
            reason = firearm ? "cf-gun-needs-firearm" : "cf-gun-needs-one-handed";
        }
        else if (IsJammed(held.Value))
        {
            reason = "cf-gun-jammed";
        }
        else
        {
            var count = new GetAmmoCountEvent();
            RaiseLocalEvent(held.Value, ref count);
            if (count.Count <= 0)
                reason = "cf-strike-action-needs-ammo";
        }

        if (reason != null)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString(reason), user, user);

            return false;
        }

        gun = held!.Value;
        return true;
    }

    /// <summary>Spends the round in the weapon, with the sound of the shot.</summary>
    private void Fire(EntityUid user, EntityUid gun)
    {
        var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();
        RaiseLocalEvent(gun, new TakeAmmoEvent(1, ammo, Transform(user).Coordinates, user));
        foreach (var (round, _) in ammo)
        {
            if (round is { } entity)
                Del(entity);
        }

        if (TryComp(gun, out GunComponent? data))
            _audio.PlayPvs(data.SoundGunshot, gun);
    }

    private bool InFirstIncrement(EntityUid user, EntityUid target, EntityUid gun)
    {
        var feet = CompOrNull<CFRangedWeaponComponent>(gun)?.RangeIncrementFeet ?? 30;
        var tiles = (_transform.GetWorldPosition(user) - _transform.GetWorldPosition(target)).Length();
        if (tiles * 5f <= feet + 2.5f)
            return true;

        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString("cf-gun-too-far"), user, user);

        return false;
    }

    private bool InReach(EntityUid user, EntityUid target)
    {
        return (_transform.GetWorldPosition(user) - _transform.GetWorldPosition(target)).Length() <= ReachTiles;
    }

    private bool IsGun(EntityUid item)
    {
        return HasComp<CFRangedWeaponComponent>(item)
               && CompOrNull<CFWeaponComponent>(item)?.Group is CFWeaponGroup.Firearm or CFWeaponGroup.Crossbow;
    }

    private bool OneHanded(EntityUid item)
    {
        return CompOrNull<CFWeaponComponent>(item)?.Hands == CFWeaponHands.One;
    }

    /// <summary>A weapon worn on the belt, back or neck that passes the test, and the slot it is in.</summary>
    private (EntityUid Item, string Slot)? Holstered(EntityUid user, Func<EntityUid, bool> wanted)
    {
        foreach (var slot in CFEncumbranceSystem.HolsterSlots)
        {
            if (_inventory.TryGetSlotEntity(user, slot, out var item)
                && HasComp<CFWeaponComponent>(item.Value)
                && !HasComp<CFShieldComponent>(item.Value)
                && wanted(item.Value))
            {
                return (item.Value, slot);
            }
        }

        return null;
    }

    private void Draw(EntityUid user, (EntityUid Item, string Slot) worn)
    {
        if (_inventory.TryUnequip(user, worn.Slot, silent: true))
            _hands.TryPickupAnyHand(user, worn.Item, animate: false);
    }

    /// <summary>Draws a worn weapon that passes the test into a free hand. Nothing happens with a weapon already in hand, unless told otherwise.</summary>
    private void DrawIf(EntityUid user, Func<EntityUid, bool> wanted, bool evenIfArmed = false)
    {
        if (_hands.CountFreeHands(user) == 0)
            return;

        if (!evenIfArmed)
        {
            foreach (var held in _hands.EnumerateHeld(user))
            {
                if (HasComp<CFWeaponComponent>(held))
                    return;
            }
        }

        if (Holstered(user, wanted) is { } worn)
            Draw(user, worn);
    }
}
