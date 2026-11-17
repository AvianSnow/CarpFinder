using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Picks that change each day: planned on the sheet, checked by the server, and swapped in by a long rest.</summary>
[TestFixture]
[TestOf(typeof(CFDailyChoiceSystem))]
public sealed class CFDailyChoiceTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

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

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    [Test]
    public async Task ALongRestSwapsTheSkillThatWasPlanned()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string first = "", second = "";
        bool trainedFirst = false, planned = false, keptBeforeRest = false, lostFirst = false, trainedSecond = false, refused = true, stays = false;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Class = "Investigator", ClassFeat = "CFFlexibleStudies" };
            var slot = CFOptionChoices.DailySlots(Server.ProtoMan, build, "Human", 1).Single();

            // Two skills nothing else in this build trains.
            var free = CFOptionChoices.Allowed(Server.ProtoMan, slot.Choice, build, "Human")
                .Where(skill => !CFOptionChoices.TrainedSkills(Server.ProtoMan, build, "Human", 1).Contains(skill))
                .ToList();
            first = free[0];
            second = free[1];
            build.OptionPicks.Add(Pick(slot.Source, slot.Slot, first));

            var hero = Built("Human", build);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var daily = SEntMan.System<CFDailyChoiceSystem>();
            var rest = SEntMan.System<CFLongRestSystem>();
            trainedFirst = combat.TrainedSkills.Contains(first);

            // A rest with no plan changes nothing.
            rest.LongRest(hero);
            stays = combat.TrainedSkills.Contains(first);

            // Something that is not one of the slot's answers, or not a daily slot at all, is refused.
            refused = !daily.TrySetPlan(hero, new List<CFOptionPick> { Pick(slot.Source, slot.Slot, "NotASkill") })
                && !daily.TrySetPlan(hero, new List<CFOptionPick> { Pick("VersatileHuman", 0, second) });

            planned = daily.TrySetPlan(hero, new List<CFOptionPick> { Pick(slot.Source, slot.Slot, second) });
            keptBeforeRest = combat.TrainedSkills.Contains(first) && !combat.TrainedSkills.Contains(second);

            rest.LongRest(hero);
            lostFirst = !combat.TrainedSkills.Contains(first);
            trainedSecond = combat.TrainedSkills.Contains(second);
        });

        Assert.Multiple(() =>
        {
            Assert.That(trainedFirst, Is.True, $"{first} is trained from the start.");
            Assert.That(stays, Is.True, "No plan, no change.");
            Assert.That(refused, Is.True, "A plan the build cannot have is refused.");
            Assert.That(planned, Is.True);
            Assert.That(keptBeforeRest, Is.True, "The plan waits for the long rest.");
            Assert.That(lostFirst, Is.True, $"{first} is given up.");
            Assert.That(trainedSecond, Is.True, $"{second} is trained now.");
        });
    }

    [Test]
    public async Task AFeyTouchedGnomeChangesItsCantrip()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string first = "", second = "";
        bool hadFirst = false, lostFirst = false, hasSecond = false;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Heritage = "FeyTouchedGnome" };
            var slot = CFOptionChoices.DailySlots(Server.ProtoMan, build, "Gnome", 1).Single();
            var cantrips = CFOptionChoices.Allowed(Server.ProtoMan, slot.Choice, build, "Gnome");
            first = cantrips[0];
            second = cantrips[1];
            build.OptionPicks.Add(Pick(slot.Source, slot.Slot, first));

            var hero = Built("Gnome", build);
            hadFirst = HasAction(hero, "CF" + first);

            SEntMan.System<CFDailyChoiceSystem>().TrySetPlan(hero, new List<CFOptionPick> { Pick(slot.Source, slot.Slot, second) });
            SEntMan.System<CFLongRestSystem>().LongRest(hero);
            lostFirst = !HasAction(hero, "CF" + first);
            hasSecond = HasAction(hero, "CF" + second);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hadFirst, Is.True, $"{first} from the start.");
            Assert.That(lostFirst, Is.True, $"{first} is gone after the rest.");
            Assert.That(hasSecond, Is.True, $"{second} is there after the rest.");
        });
    }
}
