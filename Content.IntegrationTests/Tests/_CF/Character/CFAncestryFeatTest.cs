using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Ancestry feats and the natural attacks some ancestries come with, tested on the real ones.</summary>
[TestFixture]
[TestOf(typeof(CFFeatPrototype))]
public sealed class CFAncestryFeatTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAncestryFeatHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 10
  - type: CFCombatOptions
  - type: CFActionPool

- type: entity
  parent: CFLeatherArmor
  id: CFAncestryFeatTestPlate
  components:
  - type: CFArmor
    speedPenalty: -10
    strength: 30
";

    private CFDevMap _map = default!;

    private CFReactionSystem Reactions => SEntMan.System<CFReactionSystem>();

    private EntityUid Hero(params string[] feats)
    {
        var hero = SEntMan.SpawnEntity("CFAncestryFeatHero", _map.Local(0, 0));
        foreach (var id in feats)
        {
            SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>(id).Grants, $"feat:{id}");
        }

        return hero;
    }

    private static HashSet<ProtoId<CFTraitPrototype>> Traits(string trait)
    {
        return new HashSet<ProtoId<CFTraitPrototype>> { trait };
    }

    private int UsedToday(EntityUid hero)
    {
        return SEntMan.GetComponent<CFReactionsComponent>(hero).Known[0].UsedToday;
    }

    private CFDegree Save(EntityUid hero, CFDefenseType defense, CFDegree degree, int modifier = 0, int dc = 100, string? trait = null)
    {
        var total = 10 + modifier;
        return Reactions.AnswerSave(hero, defense, 10, modifier, dc, ref total, degree, trait == null ? null : Traits(trait));
    }

    [TestCase("CFForlorn", "Emotion", 1)]
    [TestCase("CFWellGroomed", "Disease", 2)]
    [TestCase("CFLabRat", "Poison", 1)]
    public async Task SaveFeatsGiveTheirBonusAndUpgrade(string feat, string trait, int bonus)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, against = 0;
        CFDegree degree = default, other = default;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();
            var hero = Hero(feat);
            plain = conditions.DefenseModifier(hero, CFDefenseType.Fortitude);
            against = conditions.DefenseModifier(hero, CFDefenseType.Fortitude, Traits(trait));
            degree = bonuses.UpgradeSave(hero, CFDefenseType.Fortitude, CFDegree.Success, Traits(trait));
            other = bonuses.UpgradeSave(hero, CFDefenseType.Fortitude, CFDegree.Success, Traits("Curse"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(against, Is.EqualTo(plain + bonus));
            Assert.That(degree, Is.EqualTo(CFDegree.CriticalSuccess));
            Assert.That(other, Is.EqualTo(CFDegree.Success));
        });
    }

    [Test]
    public async Task HardToFoolCoversPerceptionAndWillOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int perception = 0, will = 0, fortitude = -1;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Hero("CFHardtoFool");
            int Gain(CFDefenseType defense) => conditions.DefenseModifier(hero, defense, Traits("Illusion")) - conditions.DefenseModifier(hero, defense);
            perception = Gain(CFDefenseType.Perception);
            will = Gain(CFDefenseType.Will);
            fortitude = Gain(CFDefenseType.Fortitude);
        });

        Assert.Multiple(() =>
        {
            Assert.That(perception, Is.EqualTo(1));
            Assert.That(will, Is.EqualTo(1));
            Assert.That(fortitude, Is.Zero);
        });
    }

    [Test]
    public async Task WeaponFamiliarityTrainsTheNamedWeaponsOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var familiar = CFProficiencyRank.Untrained;
        var other = CFProficiencyRank.Trained;
        var withoutFeat = CFProficiencyRank.Trained;
        await Server.WaitPost(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(Hero("CFDwarvenWeaponFamiliarity"));
            var plain = SEntMan.GetComponent<CFCombatantComponent>(Hero());

            // A creature with no class counts as trained in everything, so this has to be a class without martial weapons.
            combat.ClassId = "Wizard";
            plain.ClassId = "Wizard";
            weapons.TryStats(SEntMan.SpawnEntity("CFBattleAxe", _map.Local(0, 0)), out var axe);
            weapons.TryStats(SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0)), out var sword);
            familiar = weapons.Rank(combat, axe);
            other = weapons.Rank(combat, sword);
            withoutFeat = weapons.Rank(plain, axe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(familiar, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(other, Is.EqualTo(CFProficiencyRank.Untrained));
            Assert.That(withoutFeat, Is.EqualTo(CFProficiencyRank.Untrained));
        });
    }

    [Test]
    public async Task SuperstitionOnlyAnswersMagic()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree spell = default, mundane = default;
        var spellHasTrait = false;
        await Server.WaitPost(() =>
        {
            spell = Save(Hero("CFOrcSuperstition"), CFDefenseType.Will, CFDegree.Failure, modifier: 4, dc: 15, trait: "Magical");
            mundane = Save(Hero("CFOrcSuperstition"), CFDefenseType.Will, CFDegree.Failure, modifier: 4, dc: 15, trait: "Poison");
            var action = SEntMan.SpawnEntity("CFAcidSplash", MapCoordinates.Nullspace);
            spellHasTrait = SEntMan.System<CFEffectSystem>().SourceTraits(action).Contains("Magical");
        });

        Assert.Multiple(() =>
        {
            Assert.That(spell, Is.EqualTo(CFDegree.Success), "14 + 1 meets DC 15.");
            Assert.That(mundane, Is.EqualTo(CFDegree.Failure));
            Assert.That(spellHasTrait, Is.True, "A real spell counts as magical.");
        });
    }

    /// <summary>Against DC 100 almost every reroll is a critical failure, so the two luck feats differ exactly where the rules do.</summary>
    [Test]
    public async Task LuckRerollsAFailedSaveOncePerRest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree cat = default, halfling = default;
        int catUses = 0, usesAfterSecond = 0, otherSave = -1, spent = -1;
        await Server.WaitPost(() =>
        {
            var catfolk = Hero("CFCatsLuck");
            cat = Save(catfolk, CFDefenseType.Reflex, CFDegree.Failure);
            catUses = UsedToday(catfolk);
            spent = SEntMan.GetComponent<CFCombatOptionsComponent>(catfolk).ReactionSpent;
            Save(catfolk, CFDefenseType.Reflex, CFDegree.Failure);
            usesAfterSecond = UsedToday(catfolk);

            var fortitude = Hero("CFCatsLuck");
            Save(fortitude, CFDefenseType.Fortitude, CFDegree.Failure);
            otherSave = UsedToday(fortitude);

            // A natural 20 still lifts a reroll one step, so one of several will show the worse result.
            for (var i = 0; i < 10 && halfling != CFDegree.CriticalFailure; i++)
            {
                halfling = Save(Hero("CFHalflingLuck"), CFDefenseType.Will, CFDegree.Failure);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(cat, Is.EqualTo(CFDegree.Failure), "Cat's Luck keeps the better result.");
            Assert.That(catUses, Is.EqualTo(1));
            Assert.That(usesAfterSecond, Is.EqualTo(1), "Once per day.");
            Assert.That(spent, Is.Zero, "It is a free action, not a reaction.");
            Assert.That(otherSave, Is.Zero, "Cat's Luck is for Reflex saves.");
            Assert.That(halfling, Is.EqualTo(CFDegree.CriticalFailure), "Halfling Luck has to take the new result.");
        });
    }

    [Test]
    public async Task DwarvenDoughtinessEasesFrightenedFaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int dwarf = 0, plain = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            int After(EntityUid hero)
            {
                conditions.ApplyCondition(hero, CFConditionKind.Frightened, 3, 60f, hero, 0);
                SEntMan.EventBus.RaiseLocalEvent(hero, new CFTurnRefilledEvent());
                return conditions.Frightened(hero);
            }

            dwarf = After(Hero("CFDwarvenDoughtiness"));
            plain = After(Hero());
        });

        Assert.Multiple(() =>
        {
            Assert.That(dwarf, Is.EqualTo(1));
            Assert.That(plain, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UnburdenedIronIgnoresArmorSpeed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int dwarf = -1, plain = 0;
        await Server.WaitPost(() =>
        {
            var armor = SEntMan.System<CFArmorSystem>();
            int Penalty(EntityUid hero)
            {
                var plate = SEntMan.SpawnEntity("CFAncestryFeatTestPlate", _map.Local(0, 0));
                SEntMan.System<InventorySystem>().TryEquip(hero, plate, "outerClothing", silent: true, force: true);
                return armor.SpeedPenalty(hero);
            }

            dwarf = Penalty(Hero("CFUnburdenedIron"));
            plain = Penalty(Hero());
        });

        Assert.Multiple(() =>
        {
            Assert.That(dwarf, Is.Zero);
            Assert.That(plain, Is.LessThan(0));
        });
    }

    [TestCase("Tengu", 6, "Piercing", true, false)]
    [TestCase("Kholo", 6, "Piercing", false, false)]
    [TestCase("Lizardfolk", 4, "Slashing", true, true)]
    [TestCase("Ratfolk", 4, "Piercing", true, true)]
    public async Task SomeAncestriesComeWithANaturalAttack(string ancestry, int die, string type, bool finesse, bool agile)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFStrikeWeaponStats stats = default;
        await Server.WaitPost(() =>
        {
            var species = Server.ProtoMan.Index<CFAncestryPrototype>(ancestry).Species;
            var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(new CFCharacterBuildData()));
            stats = SEntMan.System<CFStrikeSystem>().UnarmedStats(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(stats.DamageDie, Is.EqualTo(die));
            Assert.That(stats.DamageType, Is.EqualTo(type));
            Assert.That(stats.Finesse, Is.EqualTo(finesse));
            Assert.That(stats.Agile, Is.EqualTo(agile));
        });
    }

    [Test]
    public async Task CrunchImprovesTheJawsAKholoHas()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var die = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            features.Apply(hero, Server.ProtoMan.Index<CFAncestryPrototype>("Kholo").Grants, "ancestry:Kholo");
            features.Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>("CFCrunch").Grants, "feat:CFCrunch");
            die = SEntMan.System<CFStrikeSystem>().UnarmedStats(hero).DamageDie;
        });

        Assert.That(die, Is.EqualTo(8));
    }

    /// <summary>An ancestry feat with no effect has to say so, or the builder would offer it as finished.</summary>
    [Test]
    public async Task EveryAncestryFeatWorksOrSaysTba()
    {
        var unmarked = new List<string>();
        var brokenSpells = new List<string>();
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(feat => feat.Ancestry != null))
            {
                var works = feat.Grants.Count > 0 || feat.Choices.Count > 0 || feat.TrainedSkills.Count > 0 || feat.Lores.Count > 0 || feat.NaturalDie > 0 || feat.SpeedBonus > 0;
                var text = loc.GetString(feat.Description);
                if (!works && !text.Contains("TBA") && !text.Contains("implemented later") && !text.Contains("ot applied in play"))
                    unmarked.Add(feat.ID);

                foreach (var spell in feat.Grants.OfType<CFGrantSpell>())
                {
                    if (!Server.ProtoMan.HasIndex<EntityPrototype>("CF" + spell.Spell.Id))
                        brokenSpells.Add($"{feat.ID}: {spell.Spell.Id}");
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(unmarked, Is.Empty);
            Assert.That(brokenSpells, Is.Empty);
        });
    }
}
