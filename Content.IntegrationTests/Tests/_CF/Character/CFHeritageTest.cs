using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Server.CF.ZLevels;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>What heritages give, tested on the real ones, and that none of them is unfinished without saying so.</summary>
[TestFixture]
[TestOf(typeof(CFHeritagePrototype))]
public sealed class CFHeritageTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFHeritageTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
";

    private CFDevMap _map = default!;

    private CFHeritagePrototype Heritage(string id)
    {
        return Server.ProtoMan.Index<CFHeritagePrototype>(id);
    }

    private EntityUid Hero(params string[] heritages)
    {
        var hero = SEntMan.SpawnEntity("CFHeritageTestHero", _map.Local(0, 0));
        foreach (var id in heritages)
        {
            SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Heritage(id).Grants, $"heritage:{id}");
        }

        return hero;
    }

    private EntityUid Built(string ancestry, string heritage)
    {
        var species = Server.ProtoMan.Index<CFAncestryPrototype>(ancestry).Species;
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(new CFCharacterBuildData { Heritage = heritage });
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, profile);
        return hero;
    }

    private static HashSet<ProtoId<CFTraitPrototype>> Traits(string trait)
    {
        return new HashSet<ProtoId<CFTraitPrototype>> { trait };
    }

    [Test]
    public async Task EveryResistanceHeritageResistsHalfItsLevel()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var wrong = new List<string>();
        var count = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            foreach (var heritage in Server.ProtoMan.EnumeratePrototypes<CFHeritagePrototype>())
            {
                foreach (var grant in heritage.Grants.OfType<CFGrantResistance>())
                {
                    count++;
                    var taken = bonuses.ModifyDamage(Hero(heritage.ID), grant.DamageType, 10);
                    if (taken != 9)
                        wrong.Add($"{heritage.ID}: took {taken} of 10 {grant.DamageType}");
                }
            }
        });

        Assert.That(count, Is.GreaterThan(10));
        Assert.That(wrong, Is.Empty);
    }

    [TestCase("GutsyHalfling", "Emotion", "Fear")]
    [TestCase("Dragonblood", "Fear", "Poison")]
    [TestCase("JinxedTengu", "Curse", "Poison")]
    [TestCase("SewerRat", "Disease", "Fear")]
    public async Task ASuccessBecomesACriticalAgainstTheRightTrait(string id, string trait, string otherTrait)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree against = default, other = default;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var hero = Hero(id);
            against = bonuses.UpgradeSave(hero, CFDefenseType.Will, CFDegree.Success, Traits(trait));
            other = bonuses.UpgradeSave(hero, CFDefenseType.Will, CFDegree.Success, Traits(otherTrait));
        });

        Assert.Multiple(() =>
        {
            Assert.That(against, Is.EqualTo(CFDegree.CriticalSuccess));
            Assert.That(other, Is.EqualTo(CFDegree.Success));
        });
    }

    [TestCase("SewerRat", "Poison")]
    [TestCase("ThickskinTripkee", "Disease")]
    [TestCase("GraveOrc", "Death")]
    [TestCase("RainfallOrc", "Disease")]
    public async Task ASaveBonusCountsOnlyAgainstItsTrait(string id, string trait)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, against = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Hero(id);
            plain = conditions.DefenseModifier(hero, CFDefenseType.Fortitude);
            against = conditions.DefenseModifier(hero, CFDefenseType.Fortitude, Traits(trait));
        });

        Assert.That(against, Is.EqualTo(plain + 1));
    }

    [TestCase("RazortoothGoblin", 6, "Piercing", true, false)]
    [TestCase("ClawedCatfolk", 6, "Slashing", true, true)]
    [TestCase("TalonedTengu", 4, "Slashing", true, true)]
    public async Task ANaturalAttackReplacesTheFist(string id, int die, string type, bool finesse, bool agile)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFStrikeWeaponStats stats = default;
        await Server.WaitPost(() => stats = SEntMan.System<CFStrikeSystem>().UnarmedStats(Hero(id)));

        Assert.Multiple(() =>
        {
            Assert.That(stats.DamageDie, Is.EqualTo(die));
            Assert.That(stats.DamageType, Is.EqualTo(type));
            Assert.That(stats.Finesse, Is.EqualTo(finesse));
            Assert.That(stats.Agile, Is.EqualTo(agile));
            Assert.That(stats.Traits.Nonlethal, Is.False);
        });
    }

    [Test]
    public async Task ABiggerNaturalAttackIsKept()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var die = 0;
        await Server.WaitPost(() => die = SEntMan.System<CFStrikeSystem>().UnarmedStats(Hero("RazortoothGoblin", "TalonedTengu")).DamageDie);
        Assert.That(die, Is.EqualTo(6));
    }

    [Test]
    public async Task SensesAndSpellsArrive()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var scent = 0;
        var lowLight = false;
        var hasSpell = false;
        await Server.WaitPost(() =>
        {
            scent = SEntMan.GetComponent<CFSensesComponent>(Hero("SensateGnome")).ImpreciseFeet;
            lowLight = SEntMan.GetComponent<CFSensesComponent>(Hero("TwilightHalfling")).LowLightVision;
            var seer = Hero("SeerElf");
            hasSpell = SEntMan.System<Content.Shared.Actions.SharedActionsSystem>().GetActions(seer)
                .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "CFDetectMagic");
        });

        Assert.Multiple(() =>
        {
            Assert.That(scent, Is.EqualTo(30));
            Assert.That(lowLight, Is.True);
            Assert.That(hasSpell, Is.True);
        });
    }

    [Test]
    public async Task VisionHeritagesDependOnTheAncestry()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool elfDark = false, humanDark = true, humanLow = false, elfBase = false;
        await Server.WaitPost(() =>
        {
            elfDark = SEntMan.GetComponent<CFSensesComponent>(Built("Elf", "Nephilim")).Darkvision;
            var human = SEntMan.GetComponent<CFSensesComponent>(Built("Human", "Nephilim"));
            humanDark = human.Darkvision;
            humanLow = human.LowLightVision;
            elfBase = SEntMan.GetComponent<CFSensesComponent>(Built("Elf", "WhisperElf")).LowLightVision;
        });

        Assert.Multiple(() =>
        {
            Assert.That(elfDark, Is.True, "An elf already has low-light vision, so Nephilim gives darkvision.");
            Assert.That(humanDark, Is.False);
            Assert.That(humanLow, Is.True);
            Assert.That(elfBase, Is.True, "Elves have low-light vision from the ancestry.");
        });
    }

    [Test]
    public async Task AHeritageSkillIsTrainedAndNeedsTheRightAncestry()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var rank = CFProficiencyRank.Untrained;
        var wrongAncestry = true;
        await Server.WaitPost(() =>
        {
            var orc = Built("Orc", "BattleReadyOrc");
            rank = CFDefense.SkillRank(Server.ProtoMan, SEntMan.GetComponent<CFCombatantComponent>(orc), "Intimidation");
            var build = new CFCharacterBuildData { Heritage = "BattleReadyOrc" };
            wrongAncestry = CFSkills.CollectTrainedSkills(Server.ProtoMan, build, "Elf").Contains("Intimidation");
        });

        Assert.Multiple(() =>
        {
            Assert.That(rank, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(wrongAncestry, Is.False);
        });
    }

    [Test]
    public async Task SafeFallsAndTheFreeHandTheyMayNeed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool leaf = false, freeHand = false, fullHands = true, plain = true;
        await Server.WaitPost(() =>
        {
            var zLevels = SEntMan.System<CFZLevelSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            leaf = zLevels.FallsSafely(Hero("LeafLeshy"));
            plain = zLevels.FallsSafely(Hero());

            var tripkee = Hero("WindwebTripkee");
            freeHand = zLevels.FallsSafely(tripkee);
            hands.TryPickupAnyHand(tripkee, SEntMan.SpawnEntity("CFDagger", _map.Local(0, 0)));
            hands.TryPickupAnyHand(tripkee, SEntMan.SpawnEntity("CFDagger", _map.Local(0, 0)));
            fullHands = zLevels.FallsSafely(tripkee);
        });

        Assert.Multiple(() =>
        {
            Assert.That(leaf, Is.True);
            Assert.That(plain, Is.False);
            Assert.That(freeHand, Is.True);
            Assert.That(fullHands, Is.False);
        });
    }

    /// <summary>A heritage with no effect has to say so, or the builder would offer it as finished.</summary>
    [Test]
    public async Task EveryHeritageWorksOrSaysTba()
    {
        var unmarked = new List<string>();
        var brokenSpells = new List<string>();
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            foreach (var heritage in Server.ProtoMan.EnumeratePrototypes<CFHeritagePrototype>())
            {
                var works = heritage.Grants.Count > 0
                            || heritage.TrainedSkills.Count > 0
                            || heritage.Choices.Count > 0
                            || heritage is { Darkvision: true } or { UpgradeVision: true } or { HitPoints: > 0 } or { Speed: > 0 } or { Size: not null };
                if (!works && !loc.GetString(heritage.Description).Contains("TBA"))
                    unmarked.Add(heritage.ID);

                foreach (var spell in heritage.Grants.OfType<CFGrantSpell>())
                {
                    if (!Server.ProtoMan.HasIndex<EntityPrototype>("CF" + spell.Spell.Id))
                        brokenSpells.Add($"{heritage.ID}: {spell.Spell.Id}");
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
