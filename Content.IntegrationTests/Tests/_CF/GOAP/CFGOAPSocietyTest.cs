using System;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Society;
using Content.Server.NPC.Systems;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>
/// A tribe's life, with one kobold so nothing depends on who gets there first: it eats when hungry, waters a dry
/// plot, puts food it finds into the tribe's store, and asks a stranger for food instead of attacking.
/// No language model runs here, so this is the tribe's own rules.
/// </summary>
[TestFixture]
[TestOf(typeof(CFGOAPTribeSystem))]
public sealed class CFGOAPSocietyTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSocietyTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private IConfigurationManager Config => Server.ResolveDependency<IConfigurationManager>();

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private CFGOAPTribeSystem Tribes => SEntMan.System<CFGOAPTribeSystem>();

    private SatiationSystem Satiation => SEntMan.System<SatiationSystem>();

    private async Task Load()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitPost(() => Config.SetCVar(CCVars.NPCEnabled, true));
    }

    /// <summary>A tribe of one kobold warrior, fed and watered unless the test says otherwise.</summary>
    private EntityUid Tribe(out Entity<CFGOAPTribeComponent> tribe, float hunger = 1f)
    {
        var spawner = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(0, 0));
        var group = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(spawner);
        group.Encounter = "KoboldTribe";
        group.Mobs = "CFKoboldWarrior";
        group.Amount = 1;
        SEntMan.System<CFGOAPEncounterSystem>().Spawn((spawner, group));

        tribe = (spawner, SEntMan.GetComponent<CFGOAPTribeComponent>(spawner));

        // The test sets hunger itself.
        tribe.Comp.Appetite = 1f;
        var kobold = group.Members[0];
        Feed(kobold, hunger);
        SEntMan.System<SharedPhysicsSystem>().WakeBody(kobold);
        SEntMan.System<NPCSystem>().WakeNPC(kobold);
        return kobold;
    }

    private void Feed(EntityUid uid, float hunger)
    {
        var satiation = SEntMan.GetComponent<SatiationComponent>(uid);
        var most = Satiation.GetMaximumValue((uid, satiation), SatiationSystem.Hunger) ?? 100;
        Satiation.SetValue((uid, satiation), SatiationSystem.Hunger, most * hunger);
        var wet = Satiation.GetMaximumValue((uid, satiation), SatiationSystem.Thirst) ?? 100;
        Satiation.SetValue((uid, satiation), SatiationSystem.Thirst, (float) wet);
    }

    /// <summary>Runs the game half a second at a time until <paramref name="done"/>, for at most this many seconds.</summary>
    private async Task<bool> Until(float seconds, Func<bool> done)
    {
        var finished = false;
        for (var i = 0; i < seconds * 2 && !finished; i++)
        {
            await Pair.RunTicksSync(Timing.TickRate / 2);
            await Server.WaitPost(() => finished = done());
        }

        return finished;
    }

    [Test]
    public async Task AHungryMemberEatsTheFoodItFinds()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid bread = default;
        await Server.WaitPost(() =>
        {
            bread = SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(3, 0));
            kobold = Tribe(out _, hunger: 0.1f);
        });

        var start = 0f;
        await Server.WaitPost(() => start = Tribes.Share(kobold, SatiationSystem.Hunger) ?? 1f);
        var ate = await Until(40, () => SEntMan.Deleted(bread) || (Tribes.Share(kobold, SatiationSystem.Hunger) ?? 0f) > start + 0.05f);
        Assert.That(ate, "A hungry kobold with a slice of bread three tiles away should have picked it up and eaten it.");
    }

    [Test]
    public async Task FoodItDoesNotNeedGoesIntoTheStore()
    {
        await Load();
        EntityUid bread = default;
        EntityUid crate = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(5, 1));
            bread = SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(2, 0));
            Tribe(out var tribe);
            Assert.That(tribe.Comp.Stores, Does.Contain(crate), "The tribe should know the crate beside its nest as a store.");
        });

        var stored = await Until(40, () => SEntMan.GetComponent<StorageComponent>(crate).Container.Contains(bread));
        Assert.That(stored, "A fed kobold should carry loose food to the tribe's crate and put it in.");
    }

    [Test]
    public async Task ADryPlotIsLookedAtAndThenWatered()
    {
        await Load();
        EntityUid plot = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            plot = SEntMan.SpawnEntity("HydroponicsSoil", _map.Local(4, 0));
            SEntMan.System<PlantTraySystem>().AdjustWater(plot, -1000f);
            SEntMan.SpawnEntity("CFStoneWell", _map.Local(6, 1));
            SEntMan.SpawnEntity("CFBucket", _map.Local(2, 0));
            Tribe(out tribe);
            Assert.That(tribe.Comp.Plots, Does.Contain(plot));
            Assert.That(tribe.Comp.Waters, Has.Count.EqualTo(1), "The well should count as the tribe's water.");
        });

        var looked = await Until(30, () => tribe.Comp.Soil.ContainsKey(plot));
        Assert.That(looked, "Nobody had looked at the plot, so the kobold should have gone to.");

        var watered = await Until(60, () => SEntMan.GetComponent<PlantTrayComponent>(plot).WaterLevel > 0f);
        Assert.That(watered, "It saw the plot was dry, so it should have fetched the bucket, filled it at the well and poured it.");
    }

    [Test]
    public async Task AThirstyMemberDrinksAtTheWell()
    {
        await Load();
        EntityUid kobold = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("CFStoneWell", _map.Local(5, 1));
            kobold = Tribe(out _);
            var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
            Satiation.SetValue((kobold, satiation), SatiationSystem.Thirst, 5f);
        });

        var start = 0f;
        await Server.WaitPost(() => start = Tribes.Share(kobold, SatiationSystem.Thirst) ?? 1f);
        var drank = await Until(60, () => (Tribes.Share(kobold, SatiationSystem.Thirst) ?? 0f) > start + 0.1f);
        Assert.That(drank, "A thirsty kobold should have walked to the tribe's well and drunk from it.");
    }

    /// <summary>The whole tribe: the trapper leads as the most skilled, and everyone lives by the tribe's goals.</summary>
    [Test]
    public async Task TheMostSkilledLeadsAndEveryMemberLivesAsTheTribeDoes()
    {
        await Load();
        await Server.WaitAssertion(() =>
        {
            var spawner = SEntMan.SpawnEntity("CFEncounterSpawnerKoboldTribe", _map.Local(3, 0));
            var group = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(spawner);
            SEntMan.System<CFGOAPEncounterSystem>().Spawn((spawner, group));
            var tribe = SEntMan.GetComponent<CFGOAPTribeComponent>(spawner);

            Assert.That(group.Members, Has.Count.EqualTo(6));
            Assert.That(tribe.Leader, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(tribe.Leader!.Value).EntityPrototype?.ID, Is.EqualTo("CFMobKoboldTrapper"));
            Assert.That(SEntMan.GetComponent<CFGOAPSocialComponent>(tribe.Leader.Value).Role, Is.EqualTo("Chief"));

            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var member in group.Members)
            {
                var goap = SEntMan.GetComponent<CFGOAPComponent>(member);
                Assert.That(goap.NeverSleeps, "A tribe lives on with nobody watching.");
                Assert.That(goap.Goals, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.CF.GOAP.CFGOAPGoalPrototype>("TendCrops")));
                Assert.That(goap.Actions, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.CF.GOAP.CFGOAPActionPrototype>("Parley")));
                Assert.That(names.Add(SEntMan.GetComponent<CFGOAPSocialComponent>(member).Name), "Every member has a name of its own.");
            }
        });

        // A minute of the tribe's life throws nothing.
        await Pair.RunTicksSync(Timing.TickRate * 60);
    }

    /// <summary>
    /// The whole way to a language model and back, against a stand-in server that answers as llama.cpp's does. By its
    /// own rules a hungry tribe talks first. Here the model says to defend the nest, and the leader's answer wins.
    /// </summary>
    [Test]
    public async Task TheLeaderTakesTheModelsAnswerOverItsOwnRules()
    {
        await Load();

        using var listener = new System.Net.HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string asked = null;
        var serving = Task.Run(async () =>
        {
            // The first question may be an idle one from before the stranger came. Every one gets the same answer,
            // and only the one that names the stranger can act on it.
            while (true)
            {
            var context = await listener.GetContextAsync();
            string body;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream))
            {
                body = await reader.ReadToEndAsync();
            }

            if (body.Contains("Adventurer_1"))
                asked = body;

            const string answer = "{\"goal\": \"DefendNest\", \"target\": \"Adventurer_1\", \"utterance\": \"Out of our nest!\"}";
            var reply = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = answer } } } });
            var bytes = System.Text.Encoding.UTF8.GetBytes(reply);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
            }
        });

        EntityUid hero = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlmUrl, $"http://127.0.0.1:{port}/v1/chat/completions");
            Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, true);
            Tribe(out tribe, hunger: 0.2f);
            hero = SEntMan.SpawnEntity("CFSocietyTestHero", _map.Local(6, 0));
        });

        try
        {
            // The answer comes back on another thread, in real time, so the game is given real time to get it.
            var hostile = false;
            for (var i = 0; i < 200 && !hostile; i++)
            {
                await Pair.RunTicksSync(5);
                await Task.Delay(50);
                await Server.WaitPost(() => hostile = Tribes.Stance(tribe.Comp, hero) == CFTribeStance.Hostile);
            }

            Assert.That(asked, Is.Not.Null, "The leader should have asked about the stranger.");
            Assert.That(asked, Does.Contain("\"grammar\""), "The question carries the grammar that limits the answer.");
            Assert.That(asked, Does.Contain("Adventurer_1"), "The stranger is named, so the answer can point at them.");
            Assert.That(hostile, "The model told the leader to defend the nest, so the tribe should have stopped talking.");
        }
        finally
        {
            await Server.WaitPost(() => Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, false));
            listener.Stop();
        }
    }

    /// <summary>
    /// Against the real model, started with <c>./runllm.sh</c>. Only runs when asked for by name, because it needs
    /// that server and takes as long as the model does.
    /// </summary>
    [Test, Explicit]
    public async Task ARealModelAnswersTheLeader()
    {
        await Load();
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, true);
            Tribe(out tribe, hunger: 0.2f);
            SEntMan.SpawnEntity("CFSocietyTestHero", _map.Local(6, 0));
        });

        try
        {
            for (var i = 0; i < 600 && tribe.Comp.Answers < 2; i++)
            {
                await Pair.RunTicksSync(3);
                await Task.Delay(100);
            }

            await TestContext.Out.WriteLineAsync($"Questions {tribe.Comp.Questions}, answers {tribe.Comp.Answers}, last: {tribe.Comp.LastAnswer}");
            Assert.That(tribe.Comp.Answers, Is.GreaterThanOrEqualTo(2), "The model should have given answers the game could use.");
        }
        finally
        {
            await Server.WaitPost(() => Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, false));
        }
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint) probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Test]
    public async Task AStrangerIsAskedForFoodAndFoodBuysPeace()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid hero = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            // Hungry, so it talks first even to someone standing in its nest.
            kobold = Tribe(out tribe, hunger: 0.2f);
            hero = SEntMan.SpawnEntity("CFSocietyTestHero", _map.Local(6, 0));
        });

        var goap = SEntMan.GetComponent<CFGOAPComponent>(kobold);
        var met = await Until(10, () => Tribes.Stance(tribe.Comp, hero) != null);
        Assert.That(met, "The kobold should have seen the stranger.");

        await Pair.RunTicksSync(Timing.TickRate * 4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Tribes.Stance(tribe.Comp, hero), Is.EqualTo(CFTribeStance.Parley));
            Assert.That(goap.Goal?.Id, Is.EqualTo("Parley"), "It should be standing off and talking, not fighting.");
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.EqualTo(1000), "Nobody is hurt while they talk.");
        });

        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var bread = SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(6, 0));
            Assert.That(hands.TryPickupAnyHand(hero, bread, checkActionBlocker: false));
            Assert.That(hands.TryDrop(hero, bread, checkActionBlocker: false));
            Assert.That(Tribes.Stance(tribe.Comp, hero), Is.EqualTo(CFTribeStance.Peace), "Food dropped in front of the tribe is tribute.");
        });

        await Pair.RunTicksSync(Timing.TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(goap.Target, Is.Not.EqualTo(hero), "Whoever paid is left alone.");
            Assert.That(goap.Goal?.Id, Is.Not.EqualTo("KillTarget"));
            Assert.That(goap.Goal?.Id, Is.Not.EqualTo("Parley"));
        });

        // Blood settles it, whatever was paid.
        await Server.WaitPost(() =>
        {
            var hit = new CFDamagedEvent(hero, 1);
            SEntMan.EventBus.RaiseLocalEvent(kobold, ref hit);
            Assert.That(Tribes.Stance(tribe.Comp, hero), Is.EqualTo(CFTribeStance.Hostile));
            Assert.That(Tribes.TrySetStance(tribe.Comp, hero, CFTribeStance.Parley), Is.False, "A leader cannot talk its way back from that.");
        });
    }
}
