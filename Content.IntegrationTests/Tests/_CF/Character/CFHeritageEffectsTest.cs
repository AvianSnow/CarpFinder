using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Server.CF.ZLevels;
using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Outdoors;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>
/// The heritage effects built on 16 November 2026: temperature tolerance, maneuver DCs, keener senses, falls, climbing,
/// daily actions, a chosen energy type, and the small ones.
/// </summary>
[TestFixture]
[TestOf(typeof(CFHeritagePrototype))]
public sealed class CFHeritageEffectsTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private CFDevMap _map = default!;

    private EntityUid Built(string ancestry, string heritage, params CFOptionPick[] picks)
    {
        var species = Server.ProtoMan.Index<CFAncestryPrototype>(ancestry).Species;
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Heritage = heritage };
        build.OptionPicks.AddRange(picks);
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(build));
        return hero;
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private EntityUid? Action(EntityUid hero, string id)
    {
        foreach (var action in SEntMan.System<SharedActionsSystem>().GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        return null;
    }

    [Test]
    public async Task HeatAndColdAreOneStepEasierOrHarder()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int forgeHeat = 0, forgeCold = 0, ratHeat = 0, ratCold = 0;
        await Server.WaitPost(() =>
        {
            var forge = SEntMan.GetComponent<CFTemperatureToleranceComponent>(Built("Dwarf", "ForgeDwarf"));
            forgeHeat = forge.HeatSteps;
            forgeCold = forge.ColdSteps;
            var rat = SEntMan.GetComponent<CFTemperatureToleranceComponent>(Built("Ratfolk", "DesertRat"));
            ratHeat = rat.HeatSteps;
            ratCold = rat.ColdSteps;
        });

        Assert.Multiple(() =>
        {
            Assert.That(forgeHeat, Is.EqualTo(1));
            Assert.That(forgeCold, Is.EqualTo(0));
            Assert.That(ratHeat, Is.EqualTo(1));
            Assert.That(ratCold, Is.EqualTo(-1), "A Desert Rat feels the cold more.");
            Assert.That(CFEnvironmentRules.Eased(CFTemperatureBand.SevereHeat, 1, 0), Is.EqualTo(CFTemperatureBand.MildHeat));
            Assert.That(CFEnvironmentRules.Eased(CFTemperatureBand.SevereCold, 1, 0), Is.EqualTo(CFTemperatureBand.SevereCold), "Heat steps leave cold alone.");
            Assert.That(CFEnvironmentRules.Eased(CFTemperatureBand.SevereCold, 1, -1), Is.EqualTo(CFTemperatureBand.ExtremeCold));
            Assert.That(CFEnvironmentRules.Eased(CFTemperatureBand.MildHeat, 3, 0), Is.EqualTo(CFTemperatureBand.Normal), "It never goes past normal.");
            Assert.That(CFEnvironmentRules.Eased(CFTemperatureBand.IncredibleCold, 0, -2), Is.EqualTo(CFTemperatureBand.IncredibleCold));
        });
    }

    [Test]
    public async Task HarderToMoveAndBetterAtMoving()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int rockDc = 0, kholoCheck = 0, plainDc = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            rockDc = bonuses.Total(Built("Dwarf", "RockDwarf"), CFBonusStat.ManeuverDc);
            kholoCheck = bonuses.Total(Built("Kholo", "GreatKholo"), CFBonusStat.Maneuvers);
            plainDc = bonuses.Total(Built("Dwarf", "ForgeDwarf"), CFBonusStat.ManeuverDc);
        });

        Assert.Multiple(() =>
        {
            Assert.That(rockDc, Is.EqualTo(2));
            Assert.That(kholoCheck, Is.EqualTo(1));
            Assert.That(plainDc, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task KeenerSensesFallsAndClimbs()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int seekBonus = 0, seekRange = 0, hiddenDc = 0, concealedDc = 0;
        var goblinFeet = 0;
        var goblinSafe = true;
        var elfCrit = false;
        var orcBonus = 0;
        await Server.WaitPost(() =>
        {
            var whisper = SEntMan.GetComponent<CFSeekSenseComponent>(Built("Elf", "WhisperElf"));
            seekBonus = whisper.SeekBonus;
            seekRange = whisper.RangeFeet;
            hiddenDc = whisper.HiddenFlatDc;
            concealedDc = whisper.ConcealedFlatDc;
            var goblin = Built("Goblin", "UnbreakableGoblin");
            var levels = SEntMan.System<CFZLevelSystem>();
            goblinFeet = levels.FallFeet(goblin, 40);
            goblinSafe = levels.FallsSafely(goblin);
            elfCrit = SEntMan.GetComponent<CFCombatOptionsComponent>(Built("Elf", "WoodlandElf")).ClimbSuccessCritical;
            orcBonus = SEntMan.GetComponent<CFCombatOptionsComponent>(Built("Orc", "RainfallOrc")).ClimbBonus;
        });

        Assert.Multiple(() =>
        {
            Assert.That(seekBonus, Is.EqualTo(2));
            Assert.That(seekRange, Is.EqualTo(30));
            Assert.That(hiddenDc, Is.EqualTo(9));
            Assert.That(concealedDc, Is.EqualTo(3));
            Assert.That(goblinFeet, Is.EqualTo(20), "Half the distance.");
            Assert.That(goblinSafe, Is.False, "It still hurts.");
            Assert.That(elfCrit, Is.True);
            Assert.That(orcBonus, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task DailyActionsRunOutAndComeBackWithRest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var jinx = false;
        var usesBefore = -1;
        var spentAfterUse = false;
        var usedAfterRest = -1;
        await Server.WaitPost(() =>
        {
            var halfling = Built("Halfling", "JinxedHalfling");
            jinx = HasAction(halfling, "CFActJinx");
            var action = Action(halfling, "CFActJinx")!.Value;
            var daily = SEntMan.GetComponent<CFDailyUsesComponent>(action);
            usesBefore = daily.Uses;
            daily.Used = 1;
            spentAfterUse = daily.Spent;
            SEntMan.System<CFDailyUseSystem>().Restore(halfling);
            usedAfterRest = daily.Used;
        });

        Assert.Multiple(() =>
        {
            Assert.That(jinx, Is.True);
            Assert.That(usesBefore, Is.EqualTo(1));
            Assert.That(spentAfterUse, Is.True);
            Assert.That(usedAfterRest, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AChosenEnergyTypeIsResisted()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int fire = 0, cold = 0, unpicked = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var kobold = Built("Kobold", "ElementheartKobold", new CFOptionPick { Source = "ElementheartKobold", Slot = 0, Value = "Fire" });
            fire = bonuses.ModifyDamage(kobold, "Fire", 10);
            cold = bonuses.ModifyDamage(kobold, "Cold", 10);
            unpicked = bonuses.ModifyDamage(Built("Kobold", "ElementheartKobold"), "Fire", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(fire, Is.EqualTo(9), "Half of level 1, at least 1.");
            Assert.That(cold, Is.EqualTo(10));
            Assert.That(unpicked, Is.EqualTo(10), "Nothing until the element is chosen.");
        });
    }

    [Test]
    public async Task TheSmallOnes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var strong = false;
        var fireDc = 0;
        var deadly = 0;
        var fungus = false;
        var plant = true;
        var reactions = 0;
        var whileBonuses = 0;
        var shiftColors = false;
        await Server.WaitPost(() =>
        {
            strong = SEntMan.GetComponent<CFCombatOptionsComponent>(Built("Dwarf", "StrongBloodedDwarf")).StrongBlooded;
            fireDc = SEntMan.GetComponent<CFCombatOptionsComponent>(Built("Goblin", "CharhideGoblin")).PersistentFireDc;
            deadly = SEntMan.GetComponent<CFCombatOptionsComponent>(Built("Tengu", "DogtoothTengu")).NaturalDeadly;
            var leshy = SEntMan.GetComponent<CFTraitComponent>(Built("Leshy", "FungusLeshy"));
            fungus = leshy.HasTrait("Fungus");
            plant = leshy.HasTrait("Plant");
            reactions = SEntMan.GetComponent<CFReactionsComponent>(Built("Hobgoblin", "ElfbaneHobgoblin")).Known.Count;
            whileBonuses = SEntMan.GetComponent<CFWhileBonusesComponent>(Built("Kholo", "DogKholo")).Bonuses.Count;
            shiftColors = HasAction(Built("Gnome", "ChameleonGnome"), "CFActShiftColors");
        });

        Assert.Multiple(() =>
        {
            Assert.That(strong, Is.True);
            Assert.That(fireDc, Is.EqualTo(10));
            Assert.That(deadly, Is.EqualTo(8));
            Assert.That(fungus, Is.True);
            Assert.That(plant, Is.False);
            Assert.That(reactions, Is.EqualTo(2), "Arcane first, then any magic.");
            Assert.That(whileBonuses, Is.EqualTo(1));
            Assert.That(shiftColors, Is.True);
        });
    }
}
