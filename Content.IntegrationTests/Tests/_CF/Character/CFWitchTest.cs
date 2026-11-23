using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Server.CF.Companions;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Witch: patrons and their hex cantrips, one hex a turn, familiar gifts, lessons, armaments and Sympathetic Strike.</summary>
[TestFixture]
[TestOf(typeof(CFWitchSystem))]
public sealed class CFWitchTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFTraitPrototype> Hex = "Hex";

    private static readonly ProtoId<CFClassChoicePrototype> Flamekeeper = "PatronFaithsFlamekeeper";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWitchTestHero
  components:
  - type: CFCombatant
    level: 4
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
  parent: CFWitchTestHero
  id: CFWitchTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id = "CFWitchTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
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

    private CFFeatPrototype Feat(string id)
    {
        return Server.ProtoMan.Index<CFFeatPrototype>(id);
    }

    /// <summary>A hero with a patron's grants and a familiar beside it.</summary>
    private (EntityUid Witch, EntityUid Familiar) WitchWithFamiliar(string patron)
    {
        var hero = Spawn();
        Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(patron).Grants, $"choice:{patron}");
        var familiar = Spawn("CFFamiliarBodyCat");
        SEntMan.System<CFCompanionSystem>().Bind(hero, familiar);
        return (hero, familiar);
    }

    [Test]
    public async Task EveryPatronSetsATraditionASkillAndAHexCantrip()
    {
        await Server.WaitAssertion(() =>
        {
            var spells = SEntMan.System<CFSpellSystem>();
            var seen = 0;
            foreach (var patron in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>())
            {
                if (patron.Class != "Witch")
                    continue;

                seen++;
                Assert.That(patron.Tradition, Is.Not.Null, patron.ID);
                Assert.That(patron.TrainedSkills, Has.Count.EqualTo(1), patron.ID);
                var hex = patron.Grants.OfType<Content.Shared.CF.Character.Grants.CFGrantSpell>().Single();
                Assert.That(Server.ProtoMan.TryIndex<EntityPrototype>("CF" + hex.Spell.Id, out var action), $"{patron.ID}: no action for {hex.Spell.Id}");
                Assert.That(action.TryComp<CFSpellComponent>(out var spell, SEntMan.ComponentFactory), patron.ID);
                Assert.That(spells.IsHex(spell), $"{hex.Spell.Id} is not a hex");
                Assert.That(spell.Focus, Is.False, $"{hex.Spell.Id}: a hex cantrip costs nothing");
            }

            Assert.That(seen, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task AWitchGetsThePatronsTraditionHexAndAFocusPoint()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool hex = false, other = true;
        var focus = 0;
        var tradition = CFSpellTradition.Occult;
        await Server.WaitPost(() =>
        {
            var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            var build = new CFCharacterBuildData { Class = "Witch" };
            build.ClassChoices.Add("PatronFaithsFlamekeeper");
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCFBuild(build));
            hex = HasAction(hero, "CFStokeTheHeart");
            other = HasAction(hero, "CFEvilEye");
            focus = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusPoints;
            tradition = CFClassChoices.Tradition(Server.ProtoMan, build, CFSpellTradition.Occult);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hex, Is.True);
            Assert.That(other, Is.False);
            Assert.That(focus, Is.EqualTo(1));
            Assert.That(tradition, Is.EqualTo(CFSpellTradition.Divine));
        });
    }

    [Test]
    public async Task OnlyOneHexEverySixSeconds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool ready = false, waiting = true, cantrip = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var hex = Grant(hero, "CFEvilEye");
            var daze = Grant(hero, "CFDaze");
            var spells = SEntMan.System<CFSpellSystem>();
            var book = SEntMan.EnsureComponent<CFSpellbookComponent>(hero);
            ready = spells.HasSlot(hero, SEntMan.GetComponent<CFSpellComponent>(hex));

            book.HexReadyAt = Server.ResolveDependency<IGameTiming>().CurTime + System.TimeSpan.FromSeconds(6);
            waiting = spells.HasSlot(hero, SEntMan.GetComponent<CFSpellComponent>(hex));
            cantrip = spells.HasSlot(hero, SEntMan.GetComponent<CFSpellComponent>(daze));
        });

        Assert.Multiple(() =>
        {
            Assert.That(ready, Is.True);
            Assert.That(waiting, Is.False, "A hex was cast less than 6 seconds ago");
            Assert.That(cantrip, Is.True, "Other spells are not held back");
        });
    }

    [Test]
    public async Task TheFamiliarsGiftRunsWhenAHexIsCast()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int tempHp = 0, foeAc = 0, noFamiliar = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();

            // Restored Spirit: 2 plus half the Witch's level, and the hero is level 4.
            var (flame, _) = WitchWithFamiliar("PatronFaithsFlamekeeper");
            var cast = new CFHexCastEvent(flame);
            SEntMan.EventBus.RaiseLocalEvent(flame, ref cast);
            tempHp = SEntMan.GetComponent<CFCombatantComponent>(flame).TemporaryHp;

            // Balanced Luck: the enemy nearest the familiar.
            var (spinner, _) = WitchWithFamiliar("PatronSpinnerOfThreads");
            var foe = Spawn("CFWitchTestFoe", 1);
            SEntMan.EventBus.RaiseLocalEvent(spinner, ref cast);
            foeAc = bonuses.TotalOfType(foe, CFBonusStat.ArmorClass, CFBonusType.Status);

            // No familiar, no gift.
            var alone = Spawn(x: 2);
            Features.Apply(alone, Server.ProtoMan.Index(Flamekeeper).Grants, "choice:alone");
            SEntMan.DeleteEntity(foe);
            var familiars = new List<EntityUid>();
            var query = SEntMan.AllEntityQueryEnumerator<Content.Shared.CF.Companions.CFCompanionComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                familiars.Add(uid);
            }

            foreach (var uid in familiars)
            {
                SEntMan.DeleteEntity(uid);
            }

            SEntMan.EventBus.RaiseLocalEvent(alone, ref cast);
            noFamiliar = SEntMan.GetComponent<CFCombatantComponent>(alone).TemporaryHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(tempHp, Is.EqualTo(4));
            Assert.That(foeAc, Is.EqualTo(-1));
            Assert.That(noFamiliar, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task NudgeFateTurnsOneNearMissAround()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree far = default, near = default, again = default;
        var total = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var conditions = SEntMan.System<CFConditionSystem>();
            conditions.NudgeFate(hero, 60f);

            // 5 against DC 10 fails by too much, and the hex is kept.
            var unused = 5;
            far = conditions.TryNudge(hero, CFBonusStat.Saves, 5, 0, 10, ref unused, CFDegree.Failure);

            total = 9;
            near = conditions.TryNudge(hero, CFBonusStat.Saves, 9, 0, 10, ref total, CFDegree.Failure);
            var spent = 9;
            again = conditions.TryNudge(hero, CFBonusStat.Saves, 9, 0, 10, ref spent, CFDegree.Failure);
        });

        Assert.Multiple(() =>
        {
            Assert.That(far, Is.EqualTo(CFDegree.Failure));
            Assert.That(near, Is.EqualTo(CFDegree.Success));
            Assert.That(total, Is.EqualTo(10));
            Assert.That(again, Is.EqualTo(CFDegree.Failure), "It works once");
        });
    }

    [Test]
    public async Task ALessonGivesItsHexAndAnArmamentItsAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool life = false, ward = true, asSkill = true;
        int die = 0, points = 0;
        List<string> offered = null;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var xp = SEntMan.EnsureComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFBasicLesson", Slot = 0, Value = "pick:LessonLife" });
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFWitchsArmaments", Slot = 0, Value = "pick:ArmamentTeeth" });
            Features.ApplyFeat(hero, Feat("CFBasicLesson"));
            Features.ApplyFeat(hero, Feat("CFWitchsArmaments"));
            life = HasAction(hero, "CFLifeBoost");
            ward = HasAction(hero, "CFBloodWard");
            points = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusPoints;
            die = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).NaturalDie;
            offered = CFOptionChoices.Allowed(Server.ProtoMan, Feat("CFBasicLesson").Choices[0]);

            asSkill = SEntMan.GetComponent<CFCombatantComponent>(hero).TrainedSkills.Any(skill => skill.StartsWith("pick:"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(life, Is.True);
            Assert.That(ward, Is.False);
            Assert.That(points, Is.EqualTo(1));
            Assert.That(die, Is.EqualTo(8), "Iron teeth");
            Assert.That(asSkill, Is.False, "The pick is not mistaken for a skill");
            Assert.That(offered, Is.EquivalentTo(new[] { "pick:LessonLife", "pick:LessonProtection", "pick:LessonDreams" }));
        });
    }

    [Test]
    public async Task SympatheticStrikeWeakensSavesAgainstHexes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int worst = 0, plain = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var strike = Grant(hero, "CFActSympatheticStrike");
            var bonuses = SEntMan.System<CFBonusSystem>();
            var actions = SEntMan.System<SharedActionsSystem>();
            var hexTraits = new HashSet<ProtoId<CFTraitPrototype>> { Hex };

            // Strength 40 hits unless the die shows a 1, so several attempts always include a hit.
            for (var i = 0; i < 8; i++)
            {
                var foe = Spawn("CFWitchTestFoe", 1);
                actions.ClearCooldown(strike.Owner);
                var attempt = new ActionAttemptEvent(hero, foe);
                SEntMan.EventBus.RaiseLocalEvent(strike.Owner, ref attempt);
                if (!attempt.Cancelled)
                    actions.PerformAction(hero, strike, new CFActivityTargetEvent { Target = foe });

                var against = bonuses.Total(foe, CFBonusStat.Saves, traits: hexTraits);
                if (against < worst)
                {
                    worst = against;
                    plain = bonuses.Total(foe, CFBonusStat.Saves);
                }

                SEntMan.DeleteEntity(foe);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(worst, Is.EqualTo(-1).Or.EqualTo(-2));
            Assert.That(plain, Is.EqualTo(0), "Only against hexes");
        });
    }
}
