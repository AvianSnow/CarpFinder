using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Reactions to a failed skill check: Halfling Luck's reroll, Elven Persistence's fixed result, Squawk.</summary>
[TestFixture]
[TestOf(typeof(CFReactionSystem))]
public sealed class CFSkillLuckTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSkillLuckHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 20
    currentHp: 20
    trainedSkills: [Athletics]
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private EntityUid Hero(string feat)
    {
        var hero = SEntMan.SpawnEntity("CFSkillLuckHero", _map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyFeat(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(feat)));
        return hero;
    }

    private int Used(EntityUid hero)
    {
        return SEntMan.GetComponent<CFReactionsComponent>(hero).Known.Sum(known => known.UsedToday);
    }

    [Test]
    public async Task HalflingLuckRerollsAFailedSkillCheckOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var usedAfterOne = 0;
        var usedAfterTwo = 0;
        var usedOnSuccess = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Hero("CFHalflingLuck");

            // An impossible check fails, is rerolled, and still fails: the day's luck is spent.
            conditions.RollSkillAgainst(hero, "Athletics", CFAbility.Strength, 100, out _);
            usedAfterOne = Used(hero);
            conditions.RollSkillAgainst(hero, "Athletics", CFAbility.Strength, 100, out _);
            usedAfterTwo = Used(hero);

            // A check that cannot fail never wakes it.
            var lucky = Hero("CFHalflingLuck");
            conditions.RollSkillAgainst(lucky, "Athletics", CFAbility.Strength, -20, out _);
            usedOnSuccess = Used(lucky);
        });

        Assert.Multiple(() =>
        {
            Assert.That(usedAfterOne, Is.EqualTo(1));
            Assert.That(usedAfterTwo, Is.EqualTo(1), "Once a day.");
            Assert.That(usedOnSuccess, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ElvenPersistenceTurnsAFailureIntoTwelvePlusProficiency()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var allPassed = true;
        var used = 0;
        var untrainedUsed = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Hero("CFElvenPersistence");

            // Trained at level 1 is 12 + 3 = 15 against DC 15: a natural roll either passes, or the feat makes it pass once.
            var passes = 0;
            for (var i = 0; i < 10; i++)
            {
                if (conditions.RollSkillAgainst(hero, "Athletics", CFAbility.Strength, 15, out _).IsSuccess())
                    passes++;
                if (Used(hero) > 0)
                    break;
            }

            allPassed = passes > 0;
            used = Used(hero);

            // An untrained skill gets nothing.
            var other = Hero("CFElvenPersistence");
            conditions.RollSkillAgainst(other, "Stealth", CFAbility.Dexterity, 100, out _);
            untrainedUsed = Used(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(allPassed, Is.True);
            Assert.That(used, Is.LessThanOrEqualTo(1));
            Assert.That(untrainedUsed, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task LuckyBreakWidensCatsLuck()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var save = default(CFDefenseType?);
        var skills = 0;
        var andSkills = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFCatsLuck");
            SEntMan.System<CFCharacterSpawnSystem>().ApplyFeat(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFLuckyBreak")));
            var luck = SEntMan.GetComponent<CFReactionsComponent>(hero).Known.First(known => known.Reaction.Name == "cf-feat-cats-luck").Reaction;
            save = luck.Save;
            skills = luck.Skills.Count;
            andSkills = luck.AndSkillChecks;
        });

        Assert.Multiple(() =>
        {
            Assert.That(save, Is.Null, "Any save now.");
            Assert.That(skills, Is.EqualTo(2));
            Assert.That(andSkills, Is.True);
        });
    }
}
