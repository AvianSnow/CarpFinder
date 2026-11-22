using Robust.Shared.Prototypes;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The general and skill feats that do something, a racket's key attribute, and bonuses against a trait.</summary>
[TestFixture]
[TestOf(typeof(CFCharacterFeatureSystem))]
public sealed class CFGeneralFeatTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGeneralFeatHero
  components:
  - type: CFCombatant
    level: 3
    maxHp: 30
    currentHp: 30
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private EntityUid Hero()
    {
        return SEntMan.SpawnEntity("CFGeneralFeatHero", _map.Local(0, 0));
    }

    private void Take(EntityUid hero, string feat)
    {
        SEntMan.System<CFCharacterSpawnSystem>().ApplyFeat(hero, Server.ProtoMan.Index<CFFeatPrototype>(feat));
    }

    [Test]
    public async Task ToughnessPaysBackEarlierLevelsAndCountsAtTheNext()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int taken = 0, current = 0, afterLevel = 0, dc = 0, plainDc = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var plain = Hero();
            Take(hero, "CFToughness");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            taken = combat.MaxHp;
            current = combat.CurrentHp;

            var dying = SEntMan.System<CFDyingSystem>();
            dc = dying.RecoveryDc(hero, combat);
            plainDc = dying.RecoveryDc(plain, SEntMan.GetComponent<CFCombatantComponent>(plain));

            SEntMan.System<CFExperienceSystem>().AddXpAndLevelUp(hero, CFExperienceComponent.XpPerLevel);
            afterLevel = combat.MaxHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo(33), "Level 3.");
            Assert.That(current, Is.EqualTo(33));
            Assert.That(dc, Is.EqualTo(plainDc - 1));
            Assert.That(afterLevel, Is.EqualTo(33 + 1 + 1), "No class gives 1 Hit Point on level up, and Toughness adds 1.");
        });
    }

    [Test]
    public async Task SmallFeatsDoWhatTheySay()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int deathValue = 0, plainDeath = 0, strength = 0, plainStrength = 0;
        var block = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var plain = Hero();
            Take(hero, "CFDiehard");
            Take(hero, "CFHeftyHauler");
            Take(hero, "CFShieldBlock");

            var dying = SEntMan.System<CFDyingSystem>();
            deathValue = dying.DeathValue(hero);
            plainDeath = dying.DeathValue(plain);

            var encumbrance = SEntMan.System<CFEncumbranceSystem>();
            strength = encumbrance.Strength(hero);
            plainStrength = encumbrance.Strength(plain);
            block = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ShieldBlock;
        });

        Assert.Multiple(() =>
        {
            Assert.That(deathValue, Is.EqualTo(plainDeath + 1));
            Assert.That(strength, Is.EqualTo(plainStrength + 2));
            Assert.That(block, Is.True);
        });
    }

    [Test]
    public async Task CannyAcumenRaisesThePickAndSkillTrainingTrainsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank will = default, reflex = default;
        var trained = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFCannyAcumen", Slot = 0, Value = "Will" });
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFSkillTraining", Slot = 0, Value = "Stealth" });
            Take(hero, "CFCannyAcumen");
            Take(hero, "CFSkillTraining");

            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            will = CFDefense.SaveRank(Server.ProtoMan, combat, "Will");
            reflex = CFDefense.SaveRank(Server.ProtoMan, combat, "Reflex");
            trained = combat.TrainedSkills.Contains("Stealth");
        });

        Assert.Multiple(() =>
        {
            Assert.That(will, Is.EqualTo(CFProficiencyRank.Expert));
            Assert.That(reflex, Is.LessThan(CFProficiencyRank.Expert));
            Assert.That(trained, Is.True);
        });
    }

    [Test]
    public async Task ArmorProficiencyGivesTheLightestMissing()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank light = default, medium = default;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            combat.ClassId = "Wizard";
            Take(hero, "CFArmorProficiency");
            light = CFDefense.ArmorRank(Server.ProtoMan, combat, "LightArmor");
            medium = CFDefense.ArmorRank(Server.ProtoMan, combat, "MediumArmor");
        });

        Assert.Multiple(() =>
        {
            Assert.That(light, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(medium, Is.EqualTo(CFProficiencyRank.Untrained));
        });
    }

    [Test]
    public async Task ARacketUsesThePickedKeyAttributeIfItIsOnOffer()
    {
        CFAbility unpicked = default, kept = default, cheated = default;
        await Server.WaitPost(() =>
        {
            var rogue = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Rogue"));
            CFAbility Key(string pick)
            {
                var build = new CFCharacterBuildData { Class = "Rogue" };
                build.ClassChoices.Add("Ruffian");
                if (pick != null)
                    build.OptionPicks.Add(new CFOptionPick { Source = "Ruffian", Slot = 0, Value = pick });

                return CFClassChoices.KeyAbility(Server.ProtoMan, build, rogue);
            }

            unpicked = Key(null);
            kept = Key("Dexterity");
            cheated = Key("Wisdom");
        });

        Assert.Multiple(() =>
        {
            Assert.That(unpicked, Is.EqualTo(CFAbility.Strength));
            Assert.That(kept, Is.EqualTo(CFAbility.Dexterity), "A Ruffian can keep Dexterity.");
            Assert.That(cheated, Is.EqualTo(CFAbility.Strength));
        });
    }

    [Test]
    public async Task ABonusAgainstATraitCountsOnlyAgainstThatTrait()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int undead = 0, other = 0, none = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var hero = Hero();
            bonuses.AddPermanentBonus(hero, CFBonusStat.ArmorClass, CFBonusType.Circumstance, 2, "Undead", null);

            var skeleton = Hero();
            SEntMan.EnsureComponent<CFTraitComponent>(skeleton).Traits.Add("Undead");
            var goblin = Hero();
            SEntMan.EnsureComponent<CFTraitComponent>(goblin).Traits.Add("Goblin");
            var plain = Hero();

            undead = bonuses.AgainstSource(hero, CFBonusStat.ArmorClass, skeleton);
            other = bonuses.AgainstSource(hero, CFBonusStat.ArmorClass, goblin);
            none = bonuses.AgainstSource(hero, CFBonusStat.ArmorClass, plain);
        });

        Assert.Multiple(() =>
        {
            Assert.That(undead, Is.EqualTo(2));
            Assert.That(other, Is.Zero);
            Assert.That(none, Is.Zero);
        });
    }

    [Test]
    public async Task EveryFeatThatLostItsMarkStillLoads()
    {
        var pending = -1;
        await Server.WaitPost(() =>
        {
            pending = new[]
                {
                    "CFArcaneSense", "CFArmorProficiency", "CFCannyAcumen", "CFDiehard", "CFFleet", "CFHeftyHauler",
                    "CFShieldBlock", "CFSkillTraining", "CFToughness", "CFWeaponProficiency",
                }
                .Count(id => Server.ProtoMan.Index<CFFeatPrototype>(id).PendingAction);
        });

        Assert.That(pending, Is.Zero);
    }
}
