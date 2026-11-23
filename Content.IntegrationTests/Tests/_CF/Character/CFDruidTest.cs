using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Druid's orders and order spells, Order Explorer and Order Magic, Verdant Weapon, and the morph feats it shares with the Barbarian.</summary>
[TestFixture]
[TestOf(typeof(CFClassChoices))]
public sealed class CFDruidTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly EntProtoId Cornucopia = "CFCornucopia";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFDruidTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFDruidTestHero
  id: CFDruidTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFDruidTestHero
  id: CFDruidTestAnimal
  components:
  - type: CFTrait
    traits:
    - Animal
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id = "CFDruidTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Druid(string order)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Class = "Druid" };
        build.ClassChoices.Add(order);
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCFBuild(build));
        return hero;
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        Features.GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool CanUse(EntityUid hero, EntityUid action, EntityUid target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private CFFeatPrototype Feat(string id)
    {
        return Server.ProtoMan.Index<CFFeatPrototype>(id);
    }

    private static CFCharacterBuildData Explorer(string order, string pick)
    {
        var build = new CFCharacterBuildData { Class = "Druid", ClassFeatLevel2 = "CFOrderExplorer" };
        build.ClassChoices.Add(order);
        if (pick != null)
            build.OptionPicks.Add(new CFOptionPick { Source = "CFOrderExplorer", Slot = 0, Value = pick });

        return build;
    }

    [Test]
    public async Task EachOrderGivesItsFeatAndItsSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool animal = false, leaf = false, storm = false, stormFeat = false, untamed = false, wildsong = false;
        var focus = 0;
        await Server.WaitPost(() =>
        {
            animal = HasAction(Druid("OrderAnimal"), "CFHealAnimal");
            leaf = HasAction(Druid("OrderLeaf"), "CFCornucopia");
            var stormy = Druid("OrderStorm");
            storm = HasAction(stormy, "CFTempestSurge");
            stormFeat = Features.HasFeat(stormy, "CFStormBorn");
            var wild = Druid("OrderUntamed");
            untamed = HasAction(wild, "CFUntamedForm");
            wildsong = SEntMan.GetComponent<CFLanguagesComponent>(wild).Known.Contains("Wildsong");
            focus = SEntMan.GetComponent<CFSpellbookComponent>(wild).FocusPoints;
        });

        Assert.Multiple(() =>
        {
            Assert.That(animal, Is.True, "Animal order: heal animal");
            Assert.That(leaf, Is.True, "Leaf order: cornucopia");
            Assert.That(storm, Is.True, "Storm order: tempest surge");
            Assert.That(stormFeat, Is.True, "Storm order: Storm Born");
            Assert.That(untamed, Is.True, "Untamed order: untamed form");
            Assert.That(wildsong, Is.True, "Every druid knows the Wildsong");
            Assert.That(focus, Is.EqualTo(1), "One order spell is one Focus Point");
        });
    }

    [Test]
    public async Task OrderExplorerJoinsAnotherOrder()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ProtoMan;
            var choice = Feat("CFOrderExplorer").Choices[0];
            var offered = CFOptionChoices.Allowed(prototypes, choice, Explorer("OrderStorm", null), "Human");

            Assert.Multiple(() =>
            {
                Assert.That(offered, Is.EquivalentTo(new[] { "CFAnimalCompanion", "CFLeshyFamiliar", "CFUntamedForm" }), "The other orders' level 1 feats, and not its own");
                Assert.That(CFClassChoices.MeetsRequirement(prototypes, Explorer("OrderStorm", null), Feat("CFForestPassage")), Is.False, "Not of the leaf order yet");
                Assert.That(CFClassChoices.MeetsRequirement(prototypes, Explorer("OrderStorm", "CFLeshyFamiliar"), Feat("CFForestPassage")), Is.True, "Order Explorer joins the leaf order");
                Assert.That(CFClassChoices.MeetsRequirement(prototypes, Explorer("OrderStorm", "CFLeshyFamiliar"), Feat("CFBrutalCrushDruid")), Is.False, "And no other order");
                Assert.That(CFClassChoices.MeetsRequirement(prototypes, Explorer("OrderStorm", "CFLeshyFamiliar"), Feat("CFSnowdriftSpell")), Is.True, "Its own order still counts");

                var had = CFSkills.SelectedFeats(prototypes, Explorer("OrderStorm", "CFUntamedForm"), "Human", "Druid", 2);
                Assert.That(had.Exists(feat => feat.ID == "CFUntamedForm"), Is.True, "The feat picked counts as taken");
            });
        });
    }

    [Test]
    public async Task OrderMagicGivesTheJoinedOrdersSpellOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool spell = false;
        int points = 0, again = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var xp = SEntMan.EnsureComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFOrderExplorer", Slot = 0, Value = "CFAnimalCompanion" });
            Features.ApplyFeat(hero, Feat("CFOrderMagic"));
            spell = HasAction(hero, "CFHealAnimal");
            points = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusPoints;

            // The same spell from a second option is not a second Focus Point.
            Features.GrantFocusSpell(hero, "HealAnimal");
            again = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusPoints;
        });

        Assert.Multiple(() =>
        {
            Assert.That(spell, Is.True);
            Assert.That(points, Is.EqualTo(1));
            Assert.That(again, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HealAnimalOnlyTargetsAnimals()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool animal = false, person = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var spell = Grant(hero, "CFHealAnimal");
            var effects = SEntMan.System<CFEffectSystem>();
            animal = effects.CanTarget(spell, hero, Spawn("CFDruidTestAnimal", 1));
            person = effects.CanTarget(spell, hero, Spawn("CFDruidTestHero", 2));
        });

        Assert.Multiple(() =>
        {
            Assert.That(animal, Is.True);
            Assert.That(person, Is.False);
        });
    }

    [Test]
    public async Task CornucopiaMakesOnePieceMorePerRank()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int held = 0, healDie = 0, bonus = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            if (!Server.ProtoMan.Index(Cornucopia).TryComp<CFDegreeEffectsComponent>(out var degree, SEntMan.ComponentFactory))
                return;

            SEntMan.System<CFEffectSystem>().Run(degree.Landed, new CFEffectArgs
            {
                User = hero,
                Source = hero,
                Target = hero,
                Position = SEntMan.GetComponent<TransformComponent>(hero).Coordinates,
                HeightenSteps = 1,
            });

            foreach (var item in SEntMan.System<SharedHandsSystem>().EnumerateHeld(hero))
            {
                if (SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID != "CFCornucopiaProduce")
                    continue;

                held++;
                var heal = SEntMan.GetComponent<Content.Shared.CF.Alchemy.CFHealConsumableComponent>(item);
                healDie = heal.HealDie;
                bonus = heal.HealBonus;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EqualTo(2), "Rank 2 makes two pieces, one in each hand");
            Assert.That(healDie, Is.EqualTo(6));
            Assert.That(bonus, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task CreatureComfortsEasesFearOnlyInAForm()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, morphed = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var outside = Spawn();
            Features.ApplyFeat(outside, Feat("CFCreatureComfortsDruid"));
            conditions.ApplyFrightened(outside, 2);
            plain = conditions.Frightened(outside);

            var inside = Spawn(x: 1);
            Features.ApplyFeat(inside, Feat("CFCreatureComfortsDruid"));
            SEntMan.EnsureComponent<CFBattleFormComponent>(inside);
            conditions.ApplyFrightened(inside, 2);
            morphed = conditions.Frightened(inside);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(2), "No form, no comfort");
            Assert.That(morphed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RipAndTearNeedsAMorphAttackAndTheSameTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool noMorph = true, otherTarget = true, wrongType = true, crushWrongType = true, allowed = false, crushAllowed = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFDruidTestFoe", 1);
            var other = Spawn("CFDruidTestFoe", 1);
            var rip = Grant(hero, "CFActRipAndTear");
            var crush = Grant(hero, "CFActBrutalCrush");
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            pool.MapAttacks = 1;
            pool.LastStrikeTarget = foe;
            noMorph = CanUse(hero, rip, foe);

            // An animal Rage's jaws: a morph attack that pierces.
            options.Raging = true;
            options.RageAttackDie = 10;
            options.RageAttackType = "Piercing";
            otherTarget = CanUse(hero, rip, other);
            allowed = CanUse(hero, rip, foe);
            crushWrongType = CanUse(hero, crush, foe);

            options.RageAttackType = "Bludgeoning";
            wrongType = CanUse(hero, rip, foe);
            crushAllowed = CanUse(hero, crush, foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(noMorph, Is.False, "No battle form and no Rage");
            Assert.That(otherTarget, Is.False, "Not the creature attacked last");
            Assert.That(allowed, Is.True);
            Assert.That(crushWrongType, Is.False, "Brutal Crush needs a bludgeoning attack");
            Assert.That(wrongType, Is.False, "Rip and Tear needs a piercing or slashing attack");
            Assert.That(crushAllowed, Is.True);
        });
    }

    [Test]
    public async Task BrutalCrushStupefies()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int lowest = 9, highest = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var crush = Grant(hero, "CFActBrutalCrush");
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.Raging = true;
            options.RageAttackDie = 10;
            options.RageAttackType = "Bludgeoning";

            // Strength 40 hits unless the die shows a 1, so several attempts always include a hit.
            for (var i = 0; i < 8; i++)
            {
                var foe = Spawn("CFDruidTestFoe", 1);
                pool.MapAttacks = 1;
                pool.LastStrikeTarget = foe;
                if (CanUse(hero, crush, foe))
                    SEntMan.System<SharedActionsSystem>().PerformAction(hero, crush, new CFActivityTargetEvent { Target = foe });

                var value = SEntMan.System<CFConditionSystem>().Stupefied(foe);
                if (value > 0)
                    lowest = Math.Min(lowest, value);

                highest = Math.Max(highest, value);
                SEntMan.DeleteEntity(foe);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(lowest, Is.GreaterThanOrEqualTo(2), "Stupefied 2 on a hit");
            Assert.That(highest, Is.LessThanOrEqualTo(3), "Stupefied 3 on a critical hit");
            Assert.That(highest, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task TheVerdantSeedGrowsAndReturns()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string grown = null, back = null;
        bool marked = false, wrong = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var hands = SEntMan.System<SharedHandsSystem>();
            var seeds = SEntMan.System<CFVerdantSeedSystem>();
            var seed = Spawn("CFVerdantSeed");
            hands.TryPickupAnyHand(hero, seed);
            wrong = seeds.TryGrow(hero, seed, "CFLongsword");
            seeds.TryGrow(hero, seed, "CFStaff");
            var weapon = hands.EnumerateHeld(hero).FirstOrDefault();
            grown = SEntMan.GetComponent<MetaDataComponent>(weapon).EntityPrototype?.ID;
            marked = SEntMan.TryGetComponent(weapon, out CFVerdantWeaponComponent verdant);
            if (marked)
                seeds.TryReturn(hero, (weapon, verdant));

            var after = hands.EnumerateHeld(hero).FirstOrDefault();
            back = SEntMan.GetComponent<MetaDataComponent>(after).EntityPrototype?.ID;
        });

        Assert.Multiple(() =>
        {
            Assert.That(wrong, Is.False, "Only the weapons the seed lists");
            Assert.That(grown, Is.EqualTo("CFStaff"));
            Assert.That(marked, Is.True);
            Assert.That(back, Is.EqualTo("CFVerdantSeed"));
        });
    }
}
