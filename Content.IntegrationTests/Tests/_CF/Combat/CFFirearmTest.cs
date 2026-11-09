using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Firearms and reloading, on the real items.</summary>
[TestFixture]
[TestOf(typeof(CFReloadSystem))]
public sealed class CFFirearmTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);
    private static readonly string[] Guns = { "CFFlintlockPistol", "CFFlintlockMusket", "CFArquebus", "CFBlunderbuss", "CFHandCannon" };

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFirearmHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(string id) => SEntMan.SpawnEntity(id, _map.Local(0, 0));

    private ItemSlot Slot(EntityUid gun)
    {
        SEntMan.System<ItemSlotsSystem>().TryGetSlot(gun, "projectiles", out var slot);
        return slot!;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    [Test]
    public async Task EveryFirearmIsAFirearmAndStartsLoaded()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            foreach (var id in Guns)
            {
                var gun = Spawn(id);
                Assert.That(SEntMan.GetComponent<CFWeaponComponent>(gun).Group, Is.EqualTo(CFWeaponGroup.Firearm), id);
                Assert.That(weapons.TryStats(gun, out var stats), Is.True, id);
                Assert.That(stats.Ranged, Is.True, id);
                Assert.That(stats.Reload, Is.EqualTo(1), id);
                Assert.That(Slot(gun).HasItem, Is.True, id);
            }
        });
    }

    [Test]
    public async Task TraitsAreRead()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            weapons.TryStats(Spawn("CFArquebus"), out var arquebus);
            weapons.TryStats(Spawn("CFBlunderbuss"), out var blunderbuss);
            weapons.TryStats(Spawn("CFFlintlockPistol"), out var pistol);
            Assert.That(arquebus.Traits.Kickback, Is.True);
            Assert.That(arquebus.Traits.Concussive, Is.True);
            Assert.That(arquebus.Traits.Fatal, Is.EqualTo(12));
            Assert.That(blunderbuss.Traits.Scatter, Is.EqualTo(10));
            Assert.That(pistol.Traits.Fatal, Is.EqualTo(8));
            Assert.That(pistol.Traits.Scatter, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ReloadTakesARoundFromTheOtherHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool empty = true, loaded = false, roundGone = false, noAmmo = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFFirearmHero");
            var hands = SEntMan.System<SharedHandsSystem>();
            var reload = SEntMan.System<CFReloadSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var gun = Spawn("CFFlintlockPistol");
            slots.TryEject(gun, "projectiles", null, out var ejected);
            SEntMan.DeleteEntity(ejected);
            hands.TryPickupAnyHand(hero, gun);

            empty = Slot(gun).HasItem;
            noAmmo = reload.TryReload(hero, prepaid: false);

            var round = Spawn("CFFirearmRound");
            hands.TryPickupAnyHand(hero, round);
            loaded = reload.TryReload(hero, prepaid: false);
            roundGone = Slot(gun).Item == round;
        });

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.False);
            Assert.That(noAmmo, Is.False, "Nothing to load with.");
            Assert.That(loaded, Is.True);
            Assert.That(roundGone, Is.True);
        });
    }

    [Test]
    public async Task ASlingersReloadPrepaysTheNextShot()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool plain = true, prepaid = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFFirearmHero");
            var reload = SEntMan.System<CFReloadSystem>();
            var gun = Spawn("CFFlintlockPistol");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, gun);
            var ranged = SEntMan.GetComponent<CFRangedWeaponComponent>(gun);

            reload.TryReload(hero, prepaid: false);
            plain = ranged.ReloadPrepaid;
            reload.TryReload(hero, prepaid: true);
            prepaid = ranged.ReloadPrepaid;
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.False);
            Assert.That(prepaid, Is.True);
        });
    }

    [Test]
    public async Task GunslingersAreExpertWithFirearms()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank plain = default, trained = default;
        await Server.WaitPost(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            var hero = Spawn("CFFirearmHero");
            var gun = Spawn("CFFlintlockPistol");
            weapons.TryStats(gun, out var stats);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            plain = weapons.Rank(combat, stats);
            var feature = Server.ProtoMan.Index<CFClassPrototype>("Gunslinger").Features.First(f => f.Name == "Slinger's Precision");
            features.Apply(hero, feature.Grants, "feature");
            trained = weapons.Rank(combat, stats);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.Not.EqualTo(CFProficiencyRank.Expert));
            Assert.That(trained, Is.EqualTo(CFProficiencyRank.Expert));
        });
    }

    [Test]
    public async Task EveryWayCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var granted = false;
        await Server.WaitPost(() =>
        {
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Gunslinger"))
            {
                features.Apply(Spawn("CFFirearmHero"), choice.Grants, choice.ID);
            }

            var hero = Spawn("CFFirearmHero");
            features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>("WaySniper").Grants, "sniper");
            granted = Action(hero, "CFActCoveredReload") != default;
        });

        Assert.That(granted, Is.True);
    }
}
