using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>
/// A feat given by another option (<see cref="Content.Shared.CF.Character.Grants.CFGrantFeat"/>), a feat of the player's choice
/// (<see cref="CFOptionChoiceKind.Feat"/>), and the dying rules of Nine Lives and Tough to Kill.
/// </summary>
[TestFixture]
[TestOf(typeof(CFCharacterFeatureSystem))]
public sealed class CFGrantFeatTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGrantFeatHero
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

    private static CFOptionPick Pick(string source, int slot, string value)
    {
        return new CFOptionPick { Source = source, Slot = slot, Value = value };
    }

    private EntityUid Built(string ancestry, CFCharacterBuildData build)
    {
        var species = Server.ProtoMan.Index<CFAncestryPrototype>(ancestry).Species;
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(build));
        return hero;
    }

    private bool HasFeat(EntityUid hero, string feat)
    {
        return SEntMan.System<CFCharacterFeatureSystem>().HasFeat(hero, feat);
    }

    [Test]
    public async Task AHeritageGivesAFeatThatWorks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var deathValue = 0;
        var diehard = false;
        var heftyHauler = false;
        var listed = false;
        var offeredAgain = true;
        await Server.WaitPost(() =>
        {
            var orc = Built("Orc", new CFCharacterBuildData { Heritage = "HoldScarredOrc" });
            deathValue = SEntMan.System<CFDyingSystem>().DeathValue(orc);
            diehard = HasFeat(orc, "CFDiehard");

            var android = Built("Android", new CFCharacterBuildData { Heritage = "LaborerAndroid" });
            heftyHauler = SEntMan.GetComponent<CFCombatOptionsComponent>(android).HeftyHauler;

            // The builder counts the feat as taken, so it is not offered for the level 3 general feat.
            var build = new CFCharacterBuildData { Heritage = "HoldScarredOrc" };
            listed = CFSkills.SelectedFeats(Server.ProtoMan, build, "Orc", null, 1).Any(feat => feat.ID == "CFDiehard");
            offeredAgain = CFLevelUp.FeatOptions(Server.ProtoMan, build, "Orc", CFLevelSlot.GeneralFeat, 3).Any(feat => feat.ID == "CFDiehard");
        });

        Assert.Multiple(() =>
        {
            Assert.That(deathValue, Is.EqualTo(5), "Diehard: dies at dying 5.");
            Assert.That(diehard, Is.True);
            Assert.That(heftyHauler, Is.True);
            Assert.That(listed, Is.True);
            Assert.That(offeredAgain, Is.False);
        });
    }

    [Test]
    public async Task AFeatTakenTwiceIsNotDoubled()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int once = 0, twice = 0;
        await Server.WaitPost(() =>
        {
            // Toughness through Versatile Human, and again as the level 3 general feat: hit points rise once.
            var plain = Built("Human", new CFCharacterBuildData { Heritage = "VersatileHuman" });
            var build = new CFCharacterBuildData { Heritage = "VersatileHuman", GeneralFeatLevel3 = "CFToughness" };
            build.OptionPicks.Add(Pick("VersatileHuman", 0, "CFToughness"));
            var hero = Built("Human", build);
            var spawn = SEntMan.System<CFCharacterSpawnSystem>();
            spawn.ApplyFeat(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFToughness")));
            once = SEntMan.GetComponent<CFCombatantComponent>(hero).MaxHp - SEntMan.GetComponent<CFCombatantComponent>(plain).MaxHp;
            twice = SEntMan.GetComponent<CFCombatantComponent>(hero).BonusHpPerLevel;
        });

        Assert.Multiple(() =>
        {
            Assert.That(once, Is.EqualTo(1), "Level 1, one Toughness.");
            Assert.That(twice, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task VersatileHumanChoosesAGeneralFeat()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var toughness = false;
        var allowed = new List<string>();
        var classFeatKept = true;
        var openSlot = false;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Heritage = "VersatileHuman" };
            build.OptionPicks.Add(Pick("VersatileHuman", 0, "CFToughness"));
            var hero = Built("Human", build);
            toughness = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).Toughness;

            var heritage = Server.ProtoMan.Index(new ProtoId<CFHeritagePrototype>("VersatileHuman"));
            allowed = CFOptionChoices.Allowed(Server.ProtoMan, heritage.Choices[0], build, "Human");

            var wrong = new CFCharacterBuildData { Heritage = "VersatileHuman", Class = "Fighter" };
            wrong.OptionPicks.Add(Pick("VersatileHuman", 0, "CFDoubleSlice"));
            classFeatKept = CFOptionChoices.Valid(Server.ProtoMan, wrong, "Human").Count > 0;

            openSlot = CFOptionChoices.Slots(Server.ProtoMan, new CFCharacterBuildData { Heritage = "VersatileHuman" }, "Human", 1)
                .Any(slot => slot.Source == "VersatileHuman" && slot.Choice.Kind == CFOptionChoiceKind.Feat);
        });

        Assert.Multiple(() =>
        {
            Assert.That(toughness, Is.True);
            Assert.That(allowed, Does.Contain("CFToughness"));
            Assert.That(allowed, Does.Contain("CFFleet"));
            Assert.That(allowed, Does.Not.Contain("CFDoubleSlice"), "A class feat is not a general feat.");
            Assert.That(allowed, Does.Not.Contain("CFNaturalAmbition"), "Nor is an ancestry feat.");
            Assert.That(classFeatKept, Is.False);
            Assert.That(openSlot, Is.True);
        });
    }

    [Test]
    public async Task NaturalAmbitionChoosesAClassFeat()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hasFeat = false;
        var listed = false;
        var offeredAgain = true;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Class = "Fighter", AncestryFeat = "CFNaturalAmbition" };
            build.OptionPicks.Add(Pick("CFNaturalAmbition", 0, "CFDoubleSlice"));
            var hero = Built("Human", build);
            hasFeat = HasFeat(hero, "CFDoubleSlice");
            listed = CFSkills.SelectedFeats(Server.ProtoMan, build, "Human", "Fighter", 1).Any(feat => feat.ID == "CFDoubleSlice");
            offeredAgain = CFLevelUp.FeatOptions(Server.ProtoMan, build, "Human", CFLevelSlot.ClassFeat, 2).Any(feat => feat.ID == "CFDoubleSlice");
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasFeat, Is.True);
            Assert.That(listed, Is.True);
            Assert.That(offeredAgain, Is.False);
        });
    }

    [Test]
    public async Task NineLivesIgnoresDyingOnRecoveryChecks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int dc = 0, plainDc = 0;
        await Server.WaitPost(() =>
        {
            var cat = Built("Catfolk", new CFCharacterBuildData { Heritage = "NineLivesCatfolk" });
            var plain = Built("Catfolk", new CFCharacterBuildData { Heritage = "ClawedCatfolk" });
            var dying = SEntMan.System<CFDyingSystem>();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(cat);
            var plainCombat = SEntMan.GetComponent<CFCombatantComponent>(plain);
            combat.Dying = 3;
            plainCombat.Dying = 3;
            dc = dying.RecoveryDc(cat, combat);
            plainDc = dying.RecoveryDc(plain, plainCombat);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dc, Is.EqualTo(CFDyingTrackerComponent.BaseRecoveryDc));
            Assert.That(plainDc, Is.EqualTo(CFDyingTrackerComponent.BaseRecoveryDc + 3));
        });
    }

    [Test]
    public async Task ToughToKillHoldsDyingAtTwoOncePerDay()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 0, second = 0, afterRest = 0;
        await Server.WaitPost(() =>
        {
            var hero = SEntMan.SpawnEntity("CFGrantFeatHero", _map.Local(0, 0));
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            features.SetOption(hero, CFCombatOption.ToughToKill);
            features.SetOption(hero, CFCombatOption.Diehard);
            var dying = SEntMan.System<CFDyingSystem>();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);

            // Wounded 2 and a critical knockout would be dying 4. The first time, it stays at 2.
            combat.Wounded = 2;
            dying.KnockOut(hero, combat, fromCriticalHit: true);
            first = combat.Dying;

            // The next rise is not held back.
            dying.ApplyDamageWhileDown(hero, combat, criticalHit: false);
            second = combat.Dying;

            // A long rest gives it back.
            dying.RestoreFully(hero);
            afterRest = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ToughToKillUsed ? 1 : 0;
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(2));
            Assert.That(second, Is.EqualTo(3));
            Assert.That(afterRest, Is.EqualTo(0));
        });
    }
}
