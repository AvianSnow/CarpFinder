using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Locks;
using Content.Shared.CF.Shop;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Lock;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Gunslinger: guns and ammunition in the lobby shop, misfires, Risky Reload, initial deeds and the gun feats.</summary>
[TestFixture]
[TestOf(typeof(CFGunslingerSystem))]
public sealed class CFGunslingerTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<LoadoutGroupPrototype> Weapons = "CFWeaponsMelee";
    private static readonly ProtoId<CFClassChoicePrototype> Sniper = "WaySniper";
    private static readonly EntProtoId CoverFire = "CFActCoverFire";

    private static readonly string[] Sold =
    {
        "CFFlintlockPistol", "CFHandCannon", "CFFlintlockMusket", "CFBlunderbuss", "CFArquebus",
        "CFFirearmRoundPouchFilled", "CFQuiverFilled", "CFBoltCaseFilled",
    };

    public override PoolSettings PoolSettings => new() { Connected = false };

    // The hero always hits the foe and never the wall, which has an Armor Class nothing reaches.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGunslingerTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Dexterity: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFGunslingerTestHero
  id: CFGunslingerTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFGunslingerTestFoe
  id: CFGunslingerTestWall
  components:
  - type: CFCombatant
    level: 1
    armorClass: 500
    maxHp: 5000
    currentHp: 5000
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(string id, int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();
    private CFGunslingerSystem Guns => SEntMan.System<CFGunslingerSystem>();
    private CFReloadSystem Reload => SEntMan.System<CFReloadSystem>();

    private bool Loaded(EntityUid gun)
    {
        return SEntMan.System<ItemSlotsSystem>().TryGetSlot(gun, "projectiles", out var slot) && slot.HasItem;
    }

    /// <summary>A gun in the hero's hand with nothing in it.</summary>
    private EntityUid HoldEmpty(EntityUid hero, string id)
    {
        var gun = Spawn(id);
        SEntMan.System<ItemSlotsSystem>().TryEject(gun, "projectiles", null, out var round);
        SEntMan.DeleteEntity(round);
        Hands.TryPickupAnyHand(hero, gun);
        return gun;
    }

    private void Wear(EntityUid hero, string id, string slot)
    {
        SEntMan.System<InventorySystem>().TryEquip(hero, Spawn(id), slot, silent: true, force: true);
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool Use(EntityUid hero, Entity<ActionComponent> action, EntityUid target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        if (attempt.Cancelled)
            return false;

        SEntMan.System<SharedActionsSystem>().PerformAction(hero, action, new CFActivityTargetEvent { Target = target });
        return true;
    }

    [Test]
    public async Task GunsAndAmmunitionAreInTheLobbyShop()
    {
        await Server.WaitAssertion(() =>
        {
            var group = Server.ProtoMan.Index(Weapons);
            var sold = group.Loadouts.Select(id => Server.ProtoMan.Index(id)).ToDictionary(CFCoins.LoadoutItem, loadout => loadout);
            foreach (var item in Sold)
            {
                Assert.That(sold.ContainsKey(item), Is.True, $"{item} is not in the shop");
                Assert.That(CFCoins.LoadoutPrice(Server.ProtoMan, sold[item]), Is.GreaterThan(0), $"{item} has no price, so it cannot be bought");
            }
        });
    }

    [Test]
    public async Task ABoughtPouchLoadsTheGunAndTheOtherHandHasToBeFree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool fromPouch = false, withDagger = true, withFeat = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            Wear(hero, "CFFirearmRoundPouchFilled", "belt");
            var gun = HoldEmpty(hero, "CFFlintlockPistol");
            fromPouch = Reload.TryReload(hero, prepaid: false) && Loaded(gun);

            var other = Spawn("CFGunslingerTestHero", 1);
            Wear(other, "CFFirearmRoundPouchFilled", "belt");
            HoldEmpty(other, "CFFlintlockPistol");
            Hands.TryPickupAnyHand(other, Spawn("CFDagger"));
            withDagger = Reload.TryReload(other, prepaid: false);
            SEntMan.GetComponent<CFCombatOptionsComponent>(other).DualWeaponReload = true;
            withFeat = Reload.TryReload(other, prepaid: false);
        });

        Assert.Multiple(() =>
        {
            Assert.That(fromPouch, Is.True, "A round came out of the pouch on the belt.");
            Assert.That(withDagger, Is.False, "A one-handed gun is loaded with the other hand.");
            Assert.That(withFeat, Is.True, "Dual-Weapon Reload");
        });
    }

    [Test]
    public async Task RiskyReloadLoadsAndFiresAndAMissJamsTheGun()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool used = false, jammed = false, firedEmpty = false, cleared = false, crossbow = true;
        int before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            var wall = Spawn("CFGunslingerTestWall", 2);
            Wear(hero, "CFFirearmRoundPouchFilled", "belt");
            var gun = HoldEmpty(hero, "CFFlintlockPistol");
            used = Use(hero, Grant(hero, "CFActRiskyReload"), wall);
            jammed = Guns.IsJammed(gun);
            firedEmpty = !Loaded(gun);

            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            before = (int) pool.Stamina;
            cleared = Reload.TryReload(hero, prepaid: false) && !Guns.IsJammed(gun) && Loaded(gun);
            after = (int) pool.Stamina;

            var bow = Spawn("CFCrossbow");
            Guns.Jam(bow, hero);
            crossbow = Guns.IsJammed(bow);
        });

        Assert.Multiple(() =>
        {
            Assert.That(used, Is.True, "It works with an empty gun.");
            Assert.That(firedEmpty, Is.True, "The round it loaded was fired.");
            Assert.That(jammed, Is.True, "The Strike missed.");
            Assert.That(cleared, Is.True, "Reloading clears the jam first.");
            Assert.That(before - after, Is.EqualTo(1), "Clearing a jam is 1 action.");
            Assert.That(crossbow, Is.False, "Only a firearm misfires.");
        });
    }

    [Test]
    public async Task InstantBackupDropsTheJammedGunForTheWeaponOnTheBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool holdsGun = true, holdsSword = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).InstantBackup = true;
            var sword = Spawn("CFLongsword");
            SEntMan.System<InventorySystem>().TryEquip(hero, sword, "back", silent: true, force: true);
            var gun = Spawn("CFFlintlockPistol");
            Hands.TryPickupAnyHand(hero, gun);
            Guns.Jam(gun, hero);
            holdsGun = Hands.IsHolding(hero, gun);
            holdsSword = Hands.IsHolding(hero, sword);
        });

        Assert.Multiple(() =>
        {
            Assert.That(holdsGun, Is.False);
            Assert.That(holdsSword, Is.True);
        });
    }

    [Test]
    public async Task ASniperDrawsAsTheFightBeginsAndTheFirstHitDealsMore()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool drawn = false, ready = false, spent = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            var foe = Spawn("CFGunslingerTestFoe", 2);
            SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Server.ProtoMan.Index(Sniper).Grants, "way");
            var gun = Spawn("CFFlintlockPistol");
            SEntMan.System<InventorySystem>().TryEquip(hero, gun, "belt", silent: true, force: true);

            SEntMan.System<CFFightSystem>().NoteAttack(foe, hero);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            drawn = Hands.IsHolding(hero, gun);
            ready = options.OneShotReady;

            SEntMan.System<CFWeaponSystem>().TryStats(gun, out var stats);
            SEntMan.System<CFStrikeSystem>().PerformStrike(hero, foe, stats, gun, melee: false);
            spent = options.OneShotReady;
        });

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.True, "One Shot, One Kill draws the gun off the belt.");
            Assert.That(ready, Is.True);
            Assert.That(spent, Is.False, "The hit used it up.");
        });
    }

    [Test]
    public async Task PistolTwirlCountsForAShotAndSwordAndPistolForABlade()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool plain = true, twirled = false, usedUp = true, afterShot = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            var foe = Spawn("CFGunslingerTestFoe", 1);
            var conditions = SEntMan.System<CFConditionSystem>();
            conditions.ApplyFeint(hero, foe, critical: false);
            plain = conditions.IsOffGuardTo(foe, hero, melee: false);
            conditions.FeintAlsoRanged(hero, foe);
            twirled = conditions.IsOffGuardTo(foe, hero, melee: false);
            conditions.NoteAttackRoll(hero, foe, melee: false);
            usedUp = conditions.IsOffGuardTo(foe, hero, melee: false) || conditions.IsOffGuardTo(foe, hero);

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).SwordAndPistol = true;
            var gun = Spawn("CFFlintlockPistol");
            Hands.TryPickupAnyHand(hero, gun);
            SEntMan.System<CFWeaponSystem>().TryStats(gun, out var stats);
            SEntMan.System<CFStrikeSystem>().PerformStrike(hero, foe, stats, gun, melee: false);
            afterShot = conditions.IsOffGuardTo(foe, hero) && !conditions.IsOffGuardTo(foe, hero, melee: false);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.False, "A plain Feint is for melee attacks.");
            Assert.That(twirled, Is.True);
            Assert.That(usedUp, Is.False, "It is good for one attack.");
            Assert.That(afterShot, Is.True, "Sword and Pistol: off-guard to the next melee attack only.");
        });
    }

    [Test]
    public async Task AlchemicalShotUsesTheBombUpAndBurnsTheTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool used = false, bombGone = false, burning = false, noBomb = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            var foe = Spawn("CFGunslingerTestFoe", 2);
            Hands.TryPickupAnyHand(hero, Spawn("CFFlintlockPistol"));
            var action = Grant(hero, "CFActAlchemicalShot");
            noBomb = Use(hero, action, foe);

            var bomb = Spawn("CFAlchemistsFireLesser");
            Hands.TryPickupAnyHand(hero, bomb);
            used = Use(hero, action, foe);
            bombGone = SEntMan.Deleted(bomb) || SEntMan.IsQueuedForDeletion(bomb);
            burning = SEntMan.HasComponent<CFPersistentDamageComponent>(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(noBomb, Is.False, "It needs a bomb.");
            Assert.That(used, Is.True);
            Assert.That(bombGone, Is.True);
            Assert.That(burning, Is.True, "A hit leaves persistent damage of the bomb's kind.");
        });
    }

    [Test]
    public async Task CoverFireSpoilsTheTargetsAim()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int ranged = 0, all = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero");
            var foe = Spawn("CFGunslingerTestFoe", 2);
            Hands.TryPickupAnyHand(hero, Spawn("CFFlintlockPistol"));
            Use(hero, Grant(hero, CoverFire), foe);
            var bonuses = SEntMan.System<CFBonusSystem>();
            ranged = bonuses.Total(foe, CFBonusStat.RangedAttackRoll);
            all = bonuses.Total(foe, CFBonusStat.AttackRoll);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ranged, Is.EqualTo(-2));
            Assert.That(all, Is.EqualTo(0), "Its melee attacks are untouched.");
        });
    }

    [Test]
    public async Task BlastLockOpensALockAndAMissMakesItHarder()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int penalty = 0;
        bool open = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFGunslingerTestHero", 1);
            var locks = SEntMan.System<CFLockSystem>();
            var door = SEntMan.SpawnEntity("CFWoodDoorLocked", _map.Local(0, 0));
            var comp = SEntMan.GetComponent<CFLockComponent>(door);
            locks.TryBlast((door, comp), hero, -500);
            penalty = comp.BlastPenalty;

            for (var i = 0; i < 10 && SEntMan.System<LockSystem>().IsLocked(door); i++)
            {
                locks.TryBlast((door, comp), hero, 500);
            }

            open = !SEntMan.System<LockSystem>().IsLocked(door);
        });

        Assert.Multiple(() =>
        {
            Assert.That(penalty, Is.AnyOf(2, 4), "A miss: 4 for a critical failure, 2 when a natural 20 made it a failure");
            Assert.That(open, Is.True);
        });
    }
}
