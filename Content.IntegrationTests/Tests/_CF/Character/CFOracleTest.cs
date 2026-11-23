using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Oracle: mysteries, the cursebound condition and what each curse does, and Weapon Trance.</summary>
[TestFixture]
[TestOf(typeof(CFCurseboundSystem))]
public sealed class CFOracleTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFClassChoicePrototype> Ancestors = "MysteryAncestors";
    private static readonly ProtoId<CFClassChoicePrototype> Lore = "MysteryLore";
    private static readonly ProtoId<CFClassChoicePrototype> Bones = "MysteryBones";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFOracleTestHero
  components:
  - type: CFCombatant
    level: 3
    maxHp: 500
    currentHp: 500
    classId: Oracle
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private CFCurseboundSystem Curses => SEntMan.System<CFCurseboundSystem>();

    private EntityUid Oracle(ProtoId<CFClassChoicePrototype> mystery)
    {
        var hero = SEntMan.SpawnEntity("CFOracleTestHero", _map.Local(0, 0));
        SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Server.ProtoMan.Index(mystery).Grants, $"choice:{mystery.Id}");
        return hero;
    }

    [Test]
    public async Task EveryMysteryHasACurseAndACurseboundFeat()
    {
        await Server.WaitAssertion(() =>
        {
            var seen = 0;
            foreach (var mystery in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>())
            {
                if (mystery.Class != "Oracle")
                    continue;

                seen++;
                Assert.That(mystery.Grants.OfType<CFGrantCurse>().Count(), Is.EqualTo(1), mystery.ID);
                var feat = mystery.Grants.OfType<CFGrantFeat>().Single();
                Assert.That(Server.ProtoMan.Index(feat.Feat).Grants, Is.Not.Empty, $"{mystery.ID}: {feat.Feat} does nothing");
                foreach (var spell in mystery.Grants.OfType<CFGrantFocusSpell>())
                {
                    Assert.That(Server.ProtoMan.HasIndex<EntityPrototype>("CF" + spell.Spell.Id), $"{mystery.ID}: no action for {spell.Spell}");
                }
            }

            Assert.That(seen, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task TheCurseGrowsWithEachUseAndRefocusEasesIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 0, second = 0, clumsy = 0, eased = 0, cleared = -1, clumsyAfter = -1;
        bool third = true, canAtMax = true, actionAtMax = true;
        await Server.WaitPost(() =>
        {
            var hero = Oracle(Ancestors);
            var conditions = SEntMan.System<CFConditionSystem>();
            Curses.Raise(hero);
            first = Curses.Value(hero);
            Curses.Raise(hero);
            second = Curses.Value(hero);
            clumsy = conditions.Clumsy(hero);
            third = Curses.Raise(hero);
            canAtMax = Curses.CanRaise(hero);

            // A cursebound action is refused at the most.
            var action = SEntMan.System<SharedActionsSystem>().GetActions(hero)
                .First(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == "CFActWhispersOfWeakness");
            var attempt = new ActionAttemptEvent(hero, hero);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            actionAtMax = !attempt.Cancelled;

            Curses.Lower(hero);
            eased = Curses.Value(hero);
            Curses.Clear(hero);
            cleared = Curses.Value(hero);
            clumsyAfter = conditions.Clumsy(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
            Assert.That(clumsy, Is.EqualTo(2), "Ancestors: clumsy at the cursebound value");
            Assert.That(third, Is.False, "2 is the most");
            Assert.That(canAtMax, Is.False);
            Assert.That(actionAtMax, Is.False);
            Assert.That(eased, Is.EqualTo(1), "Refocus eases it by 1");
            Assert.That(cleared, Is.EqualTo(0));
            Assert.That(clumsyAfter, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task CursesGiveTheirPenaltiesAndWeaknesses()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int perception = 0, will = 0, fortAtOne = 0, fortAtTwo = 0, weak = 0, plain = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var lore = Oracle(Lore);
            Curses.Raise(lore);
            Curses.Raise(lore);
            perception = bonuses.TotalOfType(lore, CFBonusStat.Perception, CFBonusType.Status);
            will = bonuses.TotalOfType(lore, CFBonusStat.Will, CFBonusType.Status);

            var bones = Oracle(Bones);
            plain = bonuses.ModifyDamage(bones, "Void", 10);
            Curses.Raise(bones);
            fortAtOne = bonuses.TotalOfType(bones, CFBonusStat.Fortitude, CFBonusType.Status);
            weak = bonuses.ModifyDamage(bones, "Void", 10);
            Curses.Raise(bones);
            fortAtTwo = bonuses.TotalOfType(bones, CFBonusStat.Fortitude, CFBonusType.Status);
        });

        Assert.Multiple(() =>
        {
            Assert.That(perception, Is.EqualTo(-2), "Lore: the cursebound value off Perception");
            Assert.That(will, Is.EqualTo(-2));
            Assert.That(fortAtOne, Is.EqualTo(0));
            Assert.That(fortAtTwo, Is.EqualTo(-1), "Bones at cursebound 2");
            Assert.That(weak - plain, Is.EqualTo(2), "Bones: weakness 2 to void");
        });
    }

    [Test]
    public async Task WeaponTranceMakesMartialWeaponsAsGoodAsSimpleOnes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank before = default, during = default;
        await Server.WaitPost(() =>
        {
            var hero = SEntMan.SpawnEntity("CFOracleTestHero", _map.Local(0, 0));
            var weapons = SEntMan.System<CFWeaponSystem>();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            weapons.TryStats(sword, out var stats);
            before = weapons.Rank(combat, stats);
            SEntMan.System<CFEffectSystem>().Run(new List<CFEffect> { new CFWeaponTranceEffect() }, new CFEffectArgs
            {
                User = hero,
                Source = hero,
                Target = hero,
            });
            during = weapons.Rank(combat, stats);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.LessThan(CFProficiencyRank.Trained), "An Oracle is not trained in martial weapons");
            Assert.That(during, Is.GreaterThanOrEqualTo(CFProficiencyRank.Trained));
        });
    }
}
