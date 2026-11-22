using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Critical specialization: what each weapon group does, and which characters get it with which weapons.</summary>
[TestFixture]
[TestOf(typeof(CFCriticalSpecializationSystem))]
public sealed class CFCriticalSpecializationTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // Strength 40 puts the hero's class DC out of reach, so every save against it fails.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCritSpecTestHero
  components:
  - type: CFCombatant
    thinkingTimer: 0
    level: 5
    armorClass: 12
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFCritSpecTestHero
  id: CFCritSpecTestFoe
  components:
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCriticalSpecializationSystem Crits => SEntMan.System<CFCriticalSpecializationSystem>();

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Hero(int level = 5)
    {
        var hero = SEntMan.SpawnEntity("CFCritSpecTestHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFCombatantComponent>(hero).Level = level;
        return hero;
    }

    private EntityUid Foe(int x = 1) => SEntMan.SpawnEntity("CFCritSpecTestFoe", _map.Local(x, 0));

    private CFStrikeWeaponStats Stats(string weapon)
    {
        var item = SEntMan.SpawnEntity(weapon, _map.Local(0, 0));
        SEntMan.System<CFWeaponSystem>().TryStats(item, out var stats);
        return stats;
    }

    private void Feature(EntityUid hero, string cls, string name)
    {
        var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>(cls)).Features.First(f => f.Name == name);
        SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, feature.Grants, $"feature:{name}");
    }

    private void Feat(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, $"feat:{id}");
    }

    private int Hp(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;

    [Test]
    public async Task EachGroupDoesItsOwnThing()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool sword = false, hammer = false, flail = false, bleed = false, bow = true;
        int spear = 0, brawling = 0, pick = 0, crossbowDie = 0;
        float sling = 0f;
        EntityUid hero = default, clubbed = default;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            var blade = Stats("CFLongsword");

            var target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Sword, 0);
            sword = Conditions.IsOffGuardTo(target, hero);

            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Spear, 0);
            spear = Conditions.Clumsy(target);

            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Hammer, 0);
            hammer = Conditions.IsProne(target);

            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Flail, 0);
            flail = Conditions.IsProne(target);

            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Brawling, 0);
            brawling = SEntMan.GetComponent<CFActionPoolComponent>(target).MaxActions;

            target = Foe();
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(target);
            SEntMan.System<CFActionPoolSystem>().Ensure(target);
            var before = pool.Stamina;
            Crits.Apply(hero, target, blade, CFWeaponGroup.Sling, 0);
            sling = before - pool.Stamina;

            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Knife, 0);
            bleed = SEntMan.GetComponent<CFPersistentDamageComponent>(target).Entries.Any(e => e.DamageType.Id == "Bleed" && e.Die == 6);

            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Crossbow, 0);
            crossbowDie = SEntMan.GetComponent<CFPersistentDamageComponent>(target).Entries[0].Die;

            target = Foe();
            var hp = Hp(target);
            Crits.Apply(hero, target, blade with { DamageDice = 2 }, CFWeaponGroup.Pick, 0);
            pick = hp - Hp(target);

            // No wall beside it on the arena's open floor.
            target = Foe();
            Crits.Apply(hero, target, blade, CFWeaponGroup.Bow, 0);
            bow = SEntMan.TryGetComponent(target, out CFConditionComponent cond) && cond.Immobilized;

            clubbed = Foe();
            Crits.Apply(hero, clubbed, blade, CFWeaponGroup.Club, 0);
        });
        await Pair.RunSeconds(1f);

        var pushed = 0f;
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            pushed = (xform.GetWorldPosition(clubbed) - xform.GetWorldPosition(hero)).Length();
        });

        Assert.Multiple(() =>
        {
            Assert.That(sword, Is.True, "Sword: off-guard.");
            Assert.That(spear, Is.EqualTo(1), "Spear: clumsy 1.");
            Assert.That(hammer, Is.True, "Hammer: prone on a failed Fortitude save.");
            Assert.That(flail, Is.True, "Flail: prone on a failed Reflex save.");
            Assert.That(brawling, Is.EqualTo(2), "Brawling: slowed 1.");
            Assert.That(sling, Is.EqualTo(1f), "Sling: stunned 1 is one action gone.");
            Assert.That(bleed, Is.True, "Knife: 1d6 persistent bleed.");
            Assert.That(crossbowDie, Is.EqualTo(8), "Crossbow: 1d8 persistent bleed.");
            Assert.That(pick, Is.EqualTo(4), "Pick: 2 per damage die.");
            Assert.That(bow, Is.False, "Bow: nothing to pin it to.");
            Assert.That(pushed, Is.GreaterThan(1.2f), "Club: knocked away from where it stood, 1 tile off.");
        });
    }

    [Test]
    public async Task AnAxeCarriesOnIntoTheNextEnemyItCanHit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int tooHard = -1, cleaved = 0, target = -1;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var axe = Stats("CFBattleAxe");
            var first = Foe();
            var second = SEntMan.SpawnEntity("CFCritSpecTestFoe", _map.Local(1, 1));
            var before = Hp(second);

            Crits.Apply(hero, first, axe, CFWeaponGroup.Axe, 12);
            tooHard = before - Hp(second);

            Crits.Apply(hero, first, axe, CFWeaponGroup.Axe, 13);
            cleaved = before - Hp(second);
            target = 5000 - Hp(first);
        });

        Assert.Multiple(() =>
        {
            Assert.That(tooHard, Is.Zero, "The roll has to beat the second creature's AC of 12.");
            Assert.That(cleaved, Is.GreaterThan(0), "The weapon's dice alone.");
            Assert.That(target, Is.Zero, "The first target takes nothing more.");
        });
    }

    [Test]
    public async Task AnAncestrysWeaponsGetItAtLevelFive()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFWeaponGroup? none = CFWeaponGroup.Axe, early = CFWeaponGroup.Axe, hammer = null, other = CFWeaponGroup.Axe;
        await Server.WaitPost(() =>
        {
            var foe = Foe();
            var warhammer = Stats("CFWarhammer");
            none = Crits.Applies(Hero(), foe, warhammer, true);

            var young = Hero(4);
            Feat(young, "CFDwarvenWeaponFamiliarity");
            early = Crits.Applies(young, foe, warhammer, true);

            var dwarf = Hero();
            Feat(dwarf, "CFDwarvenWeaponFamiliarity");
            hammer = Crits.Applies(dwarf, foe, warhammer, true);
            other = Crits.Applies(dwarf, foe, Stats("CFLongsword"), true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.Null, "Nobody has it without an option that gives it.");
            Assert.That(early, Is.Null, "Not before level 5.");
            Assert.That(hammer, Is.EqualTo(CFWeaponGroup.Hammer));
            Assert.That(other, Is.Null, "Only the ancestry's weapons.");
        });
    }

    [Test]
    public async Task EachClassGetsItItsOwnWay()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFWeaponGroup? calm = CFWeaponGroup.Axe, raging = null, onGuard = CFWeaponGroup.Axe, offGuard = null, heavy = CFWeaponGroup.Axe;
        CFWeaponGroup? fist = null, monkSword = CFWeaponGroup.Axe, staff = null, trained = CFWeaponGroup.Axe, master = null;
        CFWeaponGroup? stranger = CFWeaponGroup.Axe, prey = null;
        await Server.WaitPost(() =>
        {
            var foe = Foe();
            var sword = Stats("CFLongsword");
            var dagger = Stats("CFDagger");
            var strikes = SEntMan.System<CFStrikeSystem>();

            var barbarian = Hero();
            Feature(barbarian, "Barbarian", "Brutality");
            calm = Crits.Applies(barbarian, foe, sword, true);
            SEntMan.GetComponent<CFCombatOptionsComponent>(barbarian).Raging = true;
            raging = Crits.Applies(barbarian, foe, sword, true);

            var rogue = Hero();
            Feature(rogue, "Rogue", "Weapon Tricks");
            onGuard = Crits.Applies(rogue, foe, dagger, true);
            var dazed = Foe();
            Conditions.ApplyCondition(dazed, CFConditionKind.OffGuard, 0, 60f, rogue, 0);
            offGuard = Crits.Applies(rogue, dazed, dagger, true);
            heavy = Crits.Applies(rogue, dazed, sword, true);

            var monk = Hero();
            Feature(monk, "Monk", "Expert Strikes");
            fist = Crits.Applies(monk, foe, strikes.UnarmedStats(monk), true);
            monkSword = Crits.Applies(monk, foe, sword, true);

            var magus = Hero();
            Feat(magus, "CFStudentoftheStaff");
            staff = Crits.Applies(magus, foe, Stats("CFStaff"), true);

            var fighter = Hero();
            Feature(fighter, "Fighter", "Fighter Weapon Mastery");
            trained = Crits.Applies(fighter, foe, sword, true);
            SEntMan.GetComponent<CFCombatantComponent>(fighter).ProficiencyRanks["Sword"] = CFProficiencyRank.Master;
            master = Crits.Applies(fighter, foe, sword, true);

            var ranger = Hero();
            Feature(ranger, "Ranger", "Ranger Weapon Expertise");
            stranger = Crits.Applies(ranger, foe, sword, true);
            SEntMan.GetComponent<CFCombatOptionsComponent>(ranger).Prey = foe;
            prey = Crits.Applies(ranger, foe, sword, true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.Null, "A Barbarian only while raging.");
            Assert.That(raging, Is.EqualTo(CFWeaponGroup.Sword));
            Assert.That(onGuard, Is.Null, "A Rogue only against an off-guard creature.");
            Assert.That(offGuard, Is.EqualTo(CFWeaponGroup.Knife));
            Assert.That(heavy, Is.Null, "A Rogue only with an agile or finesse weapon.");
            Assert.That(fist, Is.EqualTo(CFWeaponGroup.Brawling));
            Assert.That(monkSword, Is.Null, "A Monk only unarmed.");
            Assert.That(staff, Is.EqualTo(CFWeaponGroup.Club), "Student of the Staff uses the club's effect.");
            Assert.That(trained, Is.Null, "A Fighter only as a master with the weapon.");
            Assert.That(master, Is.EqualTo(CFWeaponGroup.Sword));
            Assert.That(stranger, Is.Null, "A Ranger only against the hunted prey.");
            Assert.That(prey, Is.EqualTo(CFWeaponGroup.Sword));
        });
    }

    /// <summary>With +40 to hit against AC 12, every Strike is a critical hit.</summary>
    [Test]
    public async Task ACriticalStrikeAppliesIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool without = true, with = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var item = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            SEntMan.System<CFWeaponSystem>().TryStats(item, out var sword);
            var strikes = SEntMan.System<CFStrikeSystem>();

            var foe = Foe();
            strikes.PerformStrike(hero, foe, sword, item, melee: true, countMap: false, fixedMap: 0);
            without = Conditions.IsOffGuardTo(foe, hero);

            Crits.Add(hero, new CFCriticalSpecialization());
            foe = Foe();
            strikes.PerformStrike(hero, foe, sword, item, melee: true, countMap: false, fixedMap: 0);
            with = Conditions.IsOffGuardTo(foe, hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.False);
            Assert.That(with, Is.True, "A sword's critical hit leaves the target off-guard.");
        });
    }
}
