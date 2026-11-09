using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>Any player can take a monster from the ghost role menu, and the AI hands the body over cleanly.</summary>
[TestFixture]
[TestOf(typeof(GhostRoleSystem))]
public sealed class CFMonsterGhostRoleTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, DummyTicker = false };

    private CFDevMap _map = default!;

    /// <summary>Asked on the server thread: building the list reads localized text.</summary>
    private async Task<bool> IsListed(uint identifier)
    {
        var listed = false;
        await Server.WaitPost(() =>
        {
            listed = SEntMan.System<GhostRoleSystem>().GetGhostRolesInfo(null).Any(role => role.Identifier == identifier);
        });

        return listed;
    }

    /// <summary>
    /// A role with no <c>GhostTakeoverAvailable</c> is listed and cannot be taken, one with a stock parent's
    /// <c>prob</c> is usually deleted at map init, and one with no name reads "Unknown".
    /// Test prototypes are left out: they stand in for monsters and never reach a player.
    /// </summary>
    [Test]
    public async Task EveryMonsterHasARoleThatCanBeTaken()
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
                    || !proto.TryComp<HTNComponent>(out _, factory)
                    || !proto.TryComp<CFCombatantComponent>(out _, factory)
                    || !proto.TryComp<CFFactionComponent>(out var faction, factory)
                    || faction.Faction != "Monster")
                    continue;

                monsters++;
                if (!proto.TryComp<GhostTakeoverAvailableComponent>(out _, factory))
                    failures.Add($"{proto.ID} has no GhostTakeoverAvailable");

                if (!proto.TryComp<GhostRoleComponent>(out var role, factory))
                {
                    failures.Add($"{proto.ID} has no GhostRole");
                    continue;
                }

                if (role.Probability < 1f)
                    failures.Add($"{proto.ID} only keeps its role {role.Probability:P0} of the time");

                if (!role.RoleRules.StartsWith("You are a monster"))
                    failures.Add($"{proto.ID} has someone else's rules: {role.RoleRules}");

                if (!role.MindRoles.Contains("MindRoleGhostRoleTeamAntagonist"))
                    failures.Add($"{proto.ID} is not a monster's mind role");

                // An unknown key comes back as the key itself.
                foreach (var text in new[] { role.RoleName, role.RoleDescription, role.RoleRules })
                {
                    if (text == "Unknown" || text.StartsWith("cf-ghost"))
                        failures.Add($"{proto.ID} has a role with no text: {text}");
                }
            }
        });

        Assert.That(monsters, Is.GreaterThanOrEqualTo(11), "The monsters should all be found.");
        Assert.That(failures, Is.Empty);
    }

    [TestCase("CFMobGiantRat")]
    [TestCase("CFKoboldWarrior")]
    [TestCase("CFMobSkeletonguardRanged")]
    [TestCase("CFMobKoboldTrapper")]
    public async Task PlayerTakesAMonsterAndTheAiComesBackWhenTheyLeave(string id)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var session = Pair.Player!;

        EntityUid mob = default;
        await Server.WaitPost(() => mob = SEntMan.SpawnEntity(id, _map.Local(0, 0)));
        await Pair.RunTicksSync(30);

        var identifier = SEntMan.GetComponent<GhostRoleComponent>(mob).Identifier;
        var listed = await IsListed(identifier);
        Assert.Multiple(() =>
        {
            Assert.That(listed, "A free monster should be in the menu.");
            Assert.That(SEntMan.HasComponent<ActiveNPCComponent>(mob), "Its AI should run until someone takes it.");
        });

        var took = false;
        await Server.WaitPost(() => took = SEntMan.System<GhostRoleSystem>().Takeover(session, identifier));
        await Pair.RunTicksSync(10);

        listed = await IsListed(identifier);
        Assert.Multiple(() =>
        {
            Assert.That(took, "The role should be taken.");
            Assert.That(session.AttachedEntity, Is.EqualTo(mob));
            Assert.That(listed, Is.False, "A taken monster should leave the menu.");
            Assert.That(SEntMan.HasComponent<ActiveNPCComponent>(mob), Is.False, "The AI should let go of the body.");
            Assert.That(SEntMan.HasComponent<CFActionPoolComponent>(mob), "A player pays for actions like anyone else.");
        });

        if (SEntMan.TryGetComponent(mob, out CFGOAPComponent? brain))
        {
            Assert.Multiple(() =>
            {
                Assert.That(brain.Plan, Is.Empty, "A brain should drop its plan.");
                Assert.That(brain.Goal, Is.Null, "A brain should drop its goal.");
            });
        }

        Pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand("ghost");
        await Pair.RunTicksSync(10);

        // Each registration hands out a new identifier.
        listed = await IsListed(SEntMan.GetComponent<GhostRoleComponent>(mob).Identifier);
        Assert.Multiple(() =>
        {
            Assert.That(session.AttachedEntity, Is.Not.EqualTo(mob));
            Assert.That(listed, "A monster left alive should go back in the menu.");
            Assert.That(SEntMan.HasComponent<ActiveNPCComponent>(mob), "Its AI should take back over.");
        });
    }

    [Test]
    public async Task DeadMonsterLeavesTheMenu()
    {
        _map = await CFDevMap.Load(Pair, Footprint);

        EntityUid mob = default;
        await Server.WaitPost(() => mob = SEntMan.SpawnEntity("CFMobGiantRat", _map.Local(0, 0)));
        await Pair.RunTicksSync(5);

        var identifier = SEntMan.GetComponent<GhostRoleComponent>(mob).Identifier;
        Assert.That(await IsListed(identifier));

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead));
        await Pair.RunTicksSync(5);

        Assert.That(await IsListed(identifier), Is.False, "Nobody should be offered a corpse.");
    }
}
