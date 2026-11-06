using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CombatMode;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>The printed stat blocks, and what makes a monster different from a character.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFCreatureTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCreatureTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private (EntityUid Uid, CFCombatantComponent Combat) Creature(string id, int x = 0)
    {
        var uid = SEntMan.SpawnEntity(id, _map.Local(x, 0));
        return (uid, SEntMan.GetComponent<CFCombatantComponent>(uid));
    }

    /// <summary>id, level, AC, HP, Fort, Ref, Will, Perception, attack, damage, dice, die.</summary>
    private static readonly object[] StatBlocks =
    {
        new object[] { "CFMobGiantRat", -1, 15, 8, 6, 7, 3, 5, 7, 1, 1, 6 },
        new object[] { "CFMobBoar", 2, 15, 40, 10, 5, 8, 8, 10, 4, 2, 6 },
        new object[] { "CFMobGrizzlyBear", 3, 17, 59, 12, 6, 8, 10, 11, 4, 2, 8 },
        new object[] { "CFMobGiantFrog", 1, 15, 30, 8, 7, 5, 7, 8, 3, 1, 6 },
        new object[] { "CFMobHuntingSpider", 1, 17, 16, 6, 9, 5, 7, 9, 2, 1, 6 },
        new object[] { "CFMobSewerOoze", 1, 8, 40, 9, 1, 3, 3, 9, 1, 1, 6 },
        new object[] { "CFMobVelociraptor", 1, 16, 20, 5, 7, 4, 6, 8, 3, 1, 6 },
        new object[] { "CFMobSkeletonguardMelee", -1, 16, 4, 2, 8, 2, 2, 6, 2, 1, 4 },
        new object[] { "CFMobSkeletonguardRanged", -1, 16, 4, 2, 8, 2, 2, 6, 2, 1, 4 },
    };

    [TestCaseSource(nameof(StatBlocks))]
    public async Task StatBlockMatchesTheBook(
        string id, int level, int ac, int hp, int fort, int reflex, int will, int perception, int attack, int damage, int dice, int die)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (uid, combat) = Creature(id);
            var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(uid);
            var prototypes = Server.ProtoMan;
            var stats = SEntMan.System<CFStrikeSystem>().UnarmedStats(uid);

            Assert.Multiple(() =>
            {
                Assert.That(combat.Level, Is.EqualTo(level));
                Assert.That(combat.ArmorClass, Is.EqualTo(ac));
                Assert.That(combat.MaxHp, Is.EqualTo(hp));
                Assert.That(combat.CurrentHp, Is.EqualTo(hp));
                Assert.That(CFDefense.SaveModifier(prototypes, combat, abilities, CFDefenseType.Fortitude), Is.EqualTo(fort));
                Assert.That(CFDefense.SaveModifier(prototypes, combat, abilities, CFDefenseType.Reflex), Is.EqualTo(reflex));
                Assert.That(CFDefense.SaveModifier(prototypes, combat, abilities, CFDefenseType.Will), Is.EqualTo(will));
                Assert.That(CFDefense.PerceptionModifier(prototypes, combat, abilities), Is.EqualTo(perception));
                Assert.That(CFMelee.AttackBonus(abilities, combat, stats, true), Is.EqualTo(attack));
                Assert.That(CFMelee.DamageBonus(abilities, stats, combat), Is.EqualTo(damage));
                Assert.That(stats.DamageDice, Is.EqualTo(dice));
                Assert.That(stats.DamageDie, Is.EqualTo(die));
                Assert.That(stats.Traits.Nonlethal, Is.False, "Natural attacks are lethal.");
            });
        });
    }

    [Test]
    public async Task KoboldUsesItsPrintedSaves()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (uid, combat) = Creature("CFKoboldWarrior");
            var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(uid);
            Assert.That(CFDefense.SaveModifier(Server.ProtoMan, combat, abilities, CFDefenseType.Reflex), Is.EqualTo(7));
            Assert.That(CFDefense.PerceptionModifier(Server.ProtoMan, combat, abilities), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task MonstersDieAtZeroAndAwardXp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (rat, combat) = Creature("CFMobGiantRat");
            var hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            var before = xp.Xp;

            SEntMan.System<CFEffectSystem>().DealDamage(hero, rat, 100, "Slashing", false);

            Assert.That(SEntMan.System<MobStateSystem>().IsDead(rat), "PF2e monsters die at 0 HP.");
            Assert.That(combat.Dying, Is.Zero);
            Assert.That(xp.Xp - before, Is.EqualTo(20), "A creature two levels below the party is worth 20 XP.");
        });
    }

    [Test]
    public async Task CharactersStillGoDyingAtZero()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (rat, _) = Creature("CFMobGiantRat");
            var hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));

            SEntMan.System<CFEffectSystem>().DealDamage(rat, hero, 1000, "Slashing", false);

            Assert.That(SEntMan.System<MobStateSystem>().IsDead(hero), Is.False);
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).Dying, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ImmunitiesLastAndWork()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (ooze, combat) = (default(EntityUid), default(CFCombatantComponent)!);
        await Server.WaitPost(() => (ooze, combat) = Creature("CFMobSewerOoze"));

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            effects.DealDamage(ooze, ooze, 10, "Acid", false);
            Assert.That(combat.CurrentHp, Is.EqualTo(40), "Sewer ooze is immune to acid.");

            effects.DealDamage(ooze, ooze, 10, "Bludgeoning", false);
            Assert.That(combat.CurrentHp, Is.EqualTo(30));
        });
    }

    [Test]
    public async Task CreatureStrikeUsesItsNaturalAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (boar, _) = Creature("CFMobBoar");
            var hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            var strikes = SEntMan.System<CFStrikeSystem>();
            var defender = SEntMan.GetComponent<CFCombatantComponent>(hero);

            // AC 10 against +10: only a natural 1 misses. 2d6+4 is 6 to 16, doubled on a critical.
            for (var i = 0; i < 10 && defender.CurrentHp == 500; i++)
            {
                strikes.PerformStrike(boar, hero, strikes.UnarmedStats(boar), boar, melee: true, countMap: false);
            }

            Assert.That(500 - defender.CurrentHp, Is.InRange(6, 32));
        });
    }

    [Test]
    public async Task ARealSwingLandsAsAPathfinderStrike()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boar = default;
        EntityUid hero = default;

        await Server.WaitPost(() =>
        {
            (boar, _) = Creature("CFMobBoar");
            hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(boar, true);
        });

        var melee = SEntMan.System<SharedMeleeWeaponSystem>();
        for (var i = 0; i < 10; i++)
        {
            await Server.WaitPost(() =>
            {
                Assert.That(melee.TryGetWeapon(boar, out var weaponUid, out var weapon));
                melee.AttemptLightAttack(boar, weaponUid, weapon, hero);
            });

            await Pair.RunSeconds(1.2f);

            if (SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp < 500)
                break;
        }

        await Server.WaitAssertion(() =>
            Assert.That(500 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.InRange(6, 32)));
    }
}
