using System.Collections.Generic;
using Content.Client.CF.Crafting;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Crafting;
using Content.Server.Construction;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Preferences;
using Content.Shared.Placeable;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Crafting;

/// <summary>
/// Formulas, the Craft check and the crafting menu gate on the dev map. Materials are really placed on the bench.
/// Ability modifiers are extreme, so only a natural 1 or 20 can move the degree one step.
/// </summary>
[TestFixture]
[TestOf(typeof(CFWorkbenchSystem))]
public sealed class CFCraftingTest : GameTest
{
    private const int Extreme = 60;
    private const int Tries = 20;

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCraftTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore

- type: entity
  parent: BaseItem
  id: CFCraftTestRelic
  components:
  - type: CFItem
    level: 9

- type: craftingRecipe
  id: CFCraftTestRelicRecipe
  tag: CFRecipeAnvil
  requirements:
  - !type:CFStackRequirement
    stack: CFIronBar
  result: CFCraftTestRelic
";

    private CFDevMap _map = default!;

    private CFWorkbenchSystem Workbench => SEntMan.System<CFWorkbenchSystem>();

    private CFCraftingRecipePrototype Recipe(string id)
    {
        return Server.ProtoMan.Index<CFCraftingRecipePrototype>(id);
    }

    private EntityUid Human(int intelligence = 0, bool trained = true, int level = 1)
    {
        var mob = SEntMan.SpawnEntity("CFCraftTestHuman", _map.Local(2, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(mob).AbilityModifiers[CFAbility.Intelligence] = intelligence;
        var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
        combat.Level = level;
        if (trained)
            combat.TrainedSkills.Add(CFCraftingRules.Skill);
        return mob;
    }

    private void Teach(EntityUid mob, string formula)
    {
        var item = SEntMan.SpawnEntity(formula, _map.Local(2, 0));
        Workbench.Learn((item, SEntMan.GetComponent<CFFormulaComponent>(item)), mob);
    }

    /// <summary>A bench with materials on it, after the placer has noticed them.</summary>
    private async Task<(Entity<CFWorkbenchComponent> Bench, EntityUid Material)> Bench(string bench, string material)
    {
        Entity<CFWorkbenchComponent> station = default;
        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity(bench, _map.Local(0, 0));
            station = (uid, SEntMan.GetComponent<CFWorkbenchComponent>(uid));
            item = SEntMan.SpawnEntity(material, _map.Local(0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(item);
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(station).PlacedEntities, Does.Contain(item)));
        return (station, item);
    }

    [Test]
    public void DcsAndRanksFollowTheTables()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFCraftingRules.Dc(0, CFRarity.Common), Is.EqualTo(14));
            Assert.That(CFCraftingRules.Dc(5, CFRarity.Common), Is.EqualTo(20));
            Assert.That(CFCraftingRules.Dc(1, CFRarity.Uncommon), Is.EqualTo(17));
            Assert.That(CFCraftingRules.Dc(1, CFRarity.Rare), Is.EqualTo(20));
            Assert.That(CFCraftingRules.Dc(25, CFRarity.Common), Is.EqualTo(50));
            Assert.That(CFCraftingRules.RequiredRank(8), Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(CFCraftingRules.RequiredRank(9), Is.EqualTo(CFProficiencyRank.Master));
            Assert.That(CFCraftingRules.RequiredRank(17), Is.EqualTo(CFProficiencyRank.Legendary));
            Assert.That(CFCraftingRules.RuinedUnits(1), Is.EqualTo(1));
            Assert.That(CFCraftingRules.RuinedUnits(10), Is.EqualTo(1));
            Assert.That(CFCraftingRules.RuinedUnits(11), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task OnlyKnownRecipesForThisBenchAreListed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, _) = await Bench("CFWorkbenchAnvil", "CFIronBar1");
        await Server.WaitAssertion(() =>
        {
            var mob = Human();
            Assert.That(Workbench.Entries(bench, mob), Is.Empty);

            Teach(mob, "CFFormulaIronBuckle");
            Teach(mob, "CFFormulaCloth");

            var entries = Workbench.Entries(bench, mob);
            Assert.That(entries, Has.Count.EqualTo(1), "Cloth is made at a sewing table.");
            Assert.That(entries[0].Recipe.Id, Is.EqualTo("IronBuckle"));
            Assert.That(entries[0].HasMaterials);
            Assert.That(entries[0].Dc, Is.EqualTo(14));
        });
    }

    [Test]
    public async Task BasicCraftersBookTeachesLevelZeroButNotHigher()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = Human();
            Teach(mob, "CFBasicCraftersBook");

            var known = SEntMan.GetComponent<CFFormulaBookComponent>(mob).Known;
            Assert.That(known, Does.Contain(new ProtoId<CFCraftingRecipePrototype>("IronBuckle")));
            Assert.That(known, Does.Not.Contain(new ProtoId<CFCraftingRecipePrototype>("CFCraftTestRelicRecipe")));
        });
    }

    [Test]
    public async Task SuccessUsesMaterialsAndMakesTheResult()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, material) = await Bench("CFWorkbenchAnvil", "CFIronBar1");
        await Server.WaitAssertion(() =>
        {
            var mob = Human(intelligence: Extreme);
            Teach(mob, "CFFormulaIronBuckle");

            var degree = Workbench.TryCraft(bench, mob, Recipe("IronBuckle"));
            Assert.That(degree, Is.Not.Null);
            Assert.That(degree!.Value.IsSuccess());
            Assert.That(SEntMan.Deleted(material));
        });
    }

    [Test]
    public async Task CraftButtonRunsTheTimedCraft()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, material) = await Bench("CFWorkbenchAnvil", "CFIronBar1");
        await Server.WaitPost(() =>
        {
            var mob = Human(intelligence: Extreme);
            Teach(mob, "CFFormulaIronBuckle");

            var message = new CFWorkbenchCraftMessage("IronBuckle") { Actor = mob, UiKey = CFWorkbenchUiKey.Key };
            SEntMan.EventBus.RaiseLocalEvent(bench.Owner, message);
            Assert.That(SEntMan.Deleted(material), Is.False, "The craft takes a moment.");
        });

        await Pair.RunSeconds(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(material)));
    }

    [Test]
    public async Task UnknownRecipeCannotBeCrafted()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, material) = await Bench("CFWorkbenchAnvil", "CFIronBar1");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Workbench.TryCraft(bench, Human(intelligence: Extreme), Recipe("IronBuckle")), Is.Null);
            Assert.That(SEntMan.Deleted(material), Is.False);
        });
    }

    [Test]
    public async Task UntrainedCannotCraftEvenWithTheFormula()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, material) = await Bench("CFWorkbenchAnvil", "CFIronBar1");
        await Server.WaitAssertion(() =>
        {
            var mob = Human(intelligence: Extreme, trained: false);
            Teach(mob, "CFFormulaIronBuckle");

            Assert.That(Workbench.Entries(bench, mob)[0].Blocked, Is.EqualTo("cf-craft-blocked-untrained"));
            Assert.That(Workbench.TryCraft(bench, mob, Recipe("IronBuckle")), Is.Null);
            Assert.That(SEntMan.Deleted(material), Is.False);
        });
    }

    [Test]
    public async Task ItemLevelAndRankLimitsApply()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var recipe = Recipe("CFCraftTestRelicRecipe");
            Assert.That(Workbench.BlockedReason(Human(level: 8), recipe), Is.EqualTo("cf-craft-blocked-level"));
            Assert.That(Workbench.BlockedReason(Human(level: 9), recipe), Is.EqualTo("cf-craft-blocked-rank"), "Level 9 items need master.");
        });
    }

    [Test]
    public async Task MissingMaterialsStopTheCraft()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, _) = await Bench("CFWorkbenchAnvil", "CFIronBar1");
        await Server.WaitAssertion(() =>
        {
            var mob = Human(intelligence: Extreme);
            Teach(mob, "CFFormulaRatArmor");

            Assert.That(Workbench.Entries(bench, mob)[0].HasMaterials, Is.False, "Barding takes six bars.");
            Assert.That(Workbench.TryCraft(bench, mob, Recipe("RatArmor")), Is.Null);
        });
    }

    [Test]
    public async Task FailuresSalvageAndCriticalFailuresRuinATenth()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, material) = await Bench("CFWorkbenchAnvil", "CFIronBar10");
        await Server.WaitAssertion(() =>
        {
            var mob = Human(intelligence: -Extreme);
            Teach(mob, "CFFormulaRatArmor");
            var stack = SEntMan.GetComponent<StackComponent>(material);
            var recipe = Recipe("RatArmor");

            // A natural 20 lifts this roll to a plain failure, which must leave everything alone.
            var degree = CFDegree.Failure;
            for (var i = 0; i < Tries && degree != CFDegree.CriticalFailure; i++)
            {
                Assert.That(stack.Count, Is.EqualTo(10));
                degree = Workbench.TryCraft(bench, mob, recipe)!.Value;
                Assert.That(degree.IsSuccess(), Is.False);
            }

            Assert.That(degree, Is.EqualTo(CFDegree.CriticalFailure));
            Assert.That(stack.Count, Is.EqualTo(9), "10% of six bars rounds up to one.");
        });
    }

    [Test]
    public async Task OreSmeltsByMaterial()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (bench, material) = await Bench("CFWorkbenchFurnace", "CFOreIron10");
        await Server.WaitAssertion(() =>
        {
            var mob = Human(intelligence: Extreme);
            Teach(mob, "CFFormulaIronBar");

            Assert.That(Workbench.TryCraft(bench, mob, Recipe("IronBar")), Is.Not.Null);
            Assert.That(SEntMan.GetComponent<StackComponent>(material).Count, Is.EqualTo(5), "Five ore hold 10 iron.");
        });
    }

    [Test]
    public async Task StockCraftingNeedsTraining()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Task<bool> attempt = default!;
        await Server.WaitAssertion(() =>
        {
            var untrained = Human(trained: false);
            var crafting = SEntMan.System<CFCraftingSystem>();
            Assert.That(crafting.CanUseConstructionMenu(untrained), Is.False);
            Assert.That(crafting.CanUseConstructionMenu(Human()));
            Assert.That(crafting.CanUseConstructionMenu(_map.Grid), "Anything without PF2e stats keeps it.");

            attempt = SEntMan.System<ConstructionSystem>().TryStartItemConstruction("CFWorkbench", untrained);
        });

        Assert.That(await attempt, Is.False);
    }

    [Test]
    public async Task WindowLoadsAndShowsState()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new CFWorkbenchWindow();
            window.UpdateState(new CFWorkbenchState(new List<CFWorkbenchEntry>
            {
                new("IronBuckle", 14, true, null),
                new("Cloth", 14, false, "cf-craft-blocked-untrained"),
            }));
        });
    }
}

/// <summary>The stock crafting menu, seen from a real connected client.</summary>
[TestFixture]
[TestOf(typeof(CFConstructionGateSystem))]
public sealed class CFCraftingMenuTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCraftMenuTestHuman
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
";

    [Test]
    public async Task MenuFollowsCraftingTraining()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 2, 2));
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("CFCraftMenuTestHuman", map.Local(0, 0));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, mob);
        });
        await Pair.RunTicksSync(20);

        var construction = Client.EntMan.System<Content.Client.Construction.ConstructionSystem>();
        Assert.That(construction.CraftingEnabled, Is.False, "Untrained characters do not get the menu.");

        await Server.WaitPost(() =>
        {
            var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
            combat.TrainedSkills.Add(CFCraftingRules.Skill);
            SEntMan.Dirty(mob, combat);
        });
        await Pair.RunTicksSync(20);

        Assert.That(construction.CraftingEnabled, "Training in Crafting brings it back.");
    }
}
