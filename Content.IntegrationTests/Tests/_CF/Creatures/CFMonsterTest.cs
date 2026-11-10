using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.GOAP;
using Content.Server.NPC.HTN;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>What every CF monster needs: a GOAP brain, and a creature type to say which folder it lives in.</summary>
[TestFixture]
public sealed class CFMonsterTest : GameTest
{
    /// <summary>Traits filed as creature types that only describe one, such as Mindless. A monster needs a real type too.</summary>
    private static readonly HashSet<string> Descriptors = new()
    {
        "Amphibious", "Aquatic", "Incorporeal", "Mindless", "Swarm", "Troop", "Minion",
    };

    /// <summary>The same monsters <c>CFMonsterGhostRoleTest</c> checks. Test prototypes are left out.</summary>
    [Test]
    public async Task EveryMonsterThinksWithGoapAndHasACreatureType()
    {
        var failures = new List<string>();
        var monsters = 0;

        await Server.WaitPost(() =>
        {
            var factory = Server.ResolveDependency<IComponentFactory>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract
                    || proto.ID.Contains("Test")
                    || !proto.TryComp<HTNComponent>(out var htn, factory)
                    || !proto.TryComp<CFCombatantComponent>(out _, factory)
                    || !proto.TryComp<CFFactionComponent>(out var faction, factory)
                    || faction.Faction != "Monster")
                    continue;

                monsters++;
                if (!proto.TryComp<CFGOAPComponent>(out _, factory))
                    failures.Add($"{proto.ID} has no GOAP brain: give its base CFGOAPBrain as a parent");

                if (htn.RootTask.Task != "CFGOAPCompound")
                    failures.Add($"{proto.ID} still names {htn.RootTask.Task} as its root task");

                var hasType = proto.TryComp<CFTraitComponent>(out var traits, factory)
                              && traits.Traits.Any(t => !Descriptors.Contains(t.Id)
                                                        && prototypes.TryIndex(t, out var trait)
                                                        && trait.Category == CFTraitCategory.CreatureType);
                if (!hasType)
                    failures.Add($"{proto.ID} has no creature type on CFTrait");
            }
        });

        Assert.That(monsters, Is.GreaterThanOrEqualTo(11), "The monsters should all be found.");
        Assert.That(failures, Is.Empty);
    }
}
