using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Alchemy;
using Content.Server.CF.Character;
using Content.Server.CF.Crafting;
using Content.Server.CF.Temperature;
using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Shop;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Overlays;
using Content.Shared.Placeable;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Alchemy;

/// <summary>
/// Potions: brewed in a cauldron on a fire by someone who knows the formula, drunk for their effects, and bombs thrown
/// for a Strike that splashes. Ability modifiers are extreme, so a roll can only move one step from success.
/// </summary>
[TestFixture]
[TestOf(typeof(CFCauldronSystem))]
[TestOf(typeof(CFBombSystem))]
public sealed class CFAlchemyTest : GameTest
{
    private const int Extreme = 60;
    private const string CauldronTag = "CFRecipeCauldron";

    private static readonly Box2i Footprint = new(-1, -1, 5, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAlchemyTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: MobHuman
  id: CFAlchemyTestEnemy
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private SharedSolutionContainerSystem Solutions => SEntMan.System<SharedSolutionContainerSystem>();

    private SharedContainerSystem Containers => SEntMan.System<SharedContainerSystem>();

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private SharedInteractionSystem Interaction => SEntMan.System<SharedInteractionSystem>();

    private string Proto(EntityUid uid) => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? string.Empty;

    /// <summary>A trained crafter with both crafting feats, at (0, 0).</summary>
    private EntityUid Brewer(int intelligence = Extreme, int dexterity = 0)
    {
        var mob = SEntMan.SpawnEntity("CFAlchemyTestHuman", _map.Local(0, 0));
        var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(mob);
        abilities.AbilityModifiers[CFAbility.Intelligence] = intelligence;
        abilities.AbilityModifiers[CFAbility.Dexterity] = dexterity;
        SEntMan.GetComponent<CFCombatantComponent>(mob).TrainedSkills.Add(CFCraftingRules.Skill);
        var spawn = SEntMan.System<CFCharacterSpawnSystem>();
        spawn.ApplyFeat(mob, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFAlchemicalCrafting")));
        spawn.ApplyFeat(mob, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFMagicalCrafting")));
        return mob;
    }

    private void Teach(EntityUid mob, string formula)
    {
        var item = SEntMan.SpawnEntity(formula, _map.Local(0, 0));
        SEntMan.System<CFWorkbenchSystem>().Learn((item, SEntMan.GetComponent<CFFormulaComponent>(item)), mob);
    }

    /// <summary>A burning furnace at (1, 0) with a cauldron of water on it, after the placer has noticed it.</summary>
    private async Task<EntityUid> CauldronOnAFire(int water)
    {
        EntityUid fire = default;
        EntityUid cauldron = default;
        await Server.WaitPost(() =>
        {
            fire = SEntMan.SpawnEntity("CFFurnace", _map.Local(1, 0));
            var fuel = SEntMan.SpawnEntity("CFWoodenPlanks1", _map.Local(2, 0));
            Assert.That(SEntMan.System<CFFireplaceSystem>().TryFeed((fire, SEntMan.GetComponent<CFFireplaceComponent>(fire)), fuel, null));

            cauldron = SEntMan.SpawnEntity("CFCauldron", _map.Local(1, 0));
            Assert.That(Solutions.TryGetSolution(cauldron, "cauldron", out var soln, out _), "The cauldron should have its solution.");
            Assert.That(Solutions.TryAddReagent(soln!.Value, "Water", water, out _));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(cauldron);
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<CFFireplaceSystem>().IsBurning(fire), "The furnace should be alight.");
            Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(fire).PlacedEntities, Does.Contain(cauldron));
        });
        return cauldron;
    }

    private EntityUid PutIn(EntityUid cauldron, string ingredient)
    {
        var item = SEntMan.SpawnEntity(ingredient, _map.Local(2, 0));
        Assert.That(Containers.Insert(item, Containers.GetContainer(cauldron, "storagebase")), $"{ingredient} should go in the cauldron.");
        return item;
    }

    private List<EntityUid> Contents(EntityUid cauldron)
    {
        return new List<EntityUid>(Containers.GetContainer(cauldron, "storagebase").ContainedEntities);
    }

    private int Copper(string proto)
    {
        return Server.ProtoMan.Index<EntityPrototype>(proto).TryComp(out CFItemPriceComponent price, SEntMan.ComponentFactory) ? price.Copper : 0;
    }

    /// <summary>The user's rule: the ingredients of a potion cost about half of what the potion sells for.</summary>
    [Test]
    public async Task IngredientsCostAboutHalfThePotion()
    {
        await Server.WaitAssertion(() =>
        {
            var recipes = 0;
            Assert.Multiple(() =>
            {
                foreach (var recipe in Server.ProtoMan.EnumeratePrototypes<CFCraftingRecipePrototype>())
                {
                    if (recipe.Tag != CauldronTag || recipe.ID.Contains("Test"))
                        continue;

                    recipes++;
                    var result = Server.ProtoMan.Index<EntityPrototype>(recipe.Result);
                    Assert.That(result.TryComp(out CFItemComponent item, SEntMan.ComponentFactory) && item.Level > 0, $"{recipe.ID}: the potion needs an item level for its DC.");
                    var price = Copper(recipe.Result);
                    Assert.That(price, Is.GreaterThan(0), $"{recipe.ID}: the potion has no price.");

                    var cost = 0;
                    foreach (var requirement in recipe.Requirements)
                    {
                        Assert.That(requirement, Is.TypeOf<CFItemRequirement>(), $"{recipe.ID}: a brew names its ingredients one by one.");
                        if (requirement is CFItemRequirement ingredient)
                        {
                            var each = Copper(ingredient.ProtoId);
                            Assert.That(each, Is.GreaterThan(0), $"{recipe.ID}: {ingredient.ProtoId} has no price.");
                            cost += each * ingredient.Count;
                        }
                    }

                    Assert.That(cost, Is.InRange(price * 0.4, price * 0.6), $"{recipe.ID}: ingredients {cost} against a price of {price}.");
                }
            });

            Assert.That(recipes, Is.GreaterThanOrEqualTo(10), "The ten potions from the design should be brewed.");
        });
    }

    [Test]
    public async Task ACauldronOnAFireBrewsForABrewerWhoKnowsTheFormula()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var cauldron = await CauldronOnAFire(30);
        EntityUid brewer = default;
        EntityUid needle = default;
        await Server.WaitPost(() =>
        {
            brewer = Brewer();
            Teach(brewer, "CFFormulaElixirOfLifeMinor");
            PutIn(cauldron, "CFWildSage");
            PutIn(cauldron, "CFBlueAmanita");

            // The last ingredient goes in by hand, which makes them the brewer.
            needle = SEntMan.SpawnEntity("CFSilverNeedle", _map.Local(0, 0));
            Assert.That(Hands.TryPickupAnyHand(brewer, needle, checkActionBlocker: false));
            Interaction.InteractUsing(brewer, needle, cauldron, SEntMan.GetComponent<TransformComponent>(cauldron).Coordinates, checkCanInteract: false, checkCanUse: false);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(Contents(cauldron), Does.Contain(needle), "Clicking the cauldron with an ingredient puts it in.");
            var comp = SEntMan.GetComponent<CFCauldronComponent>(cauldron);
            Assert.That(comp.Brewer, Is.EqualTo(brewer));
            var recipe = SEntMan.System<CFCauldronSystem>().FindRecipe((cauldron, comp), new HashSet<EntityUid>(Contents(cauldron)));
            Assert.That(recipe?.ID, Is.EqualTo("ElixirOfLifeMinor"));
        });

        var time = Server.ProtoMan.Index(new ProtoId<CFCraftingRecipePrototype>("ElixirOfLifeMinor")).CraftTime.TotalSeconds;
        await Pair.RunTicksSync((int) (Timing.TickRate * (time + 6)));

        await Server.WaitAssertion(() =>
        {
            // A critical success brews one more, and this brewer rolls one on anything but a natural 1.
            var contents = Contents(cauldron).ConvertAll(Proto);
            Assert.That(contents, Has.Count.InRange(1, 2), "The herbs are gone and the elixir is in the cauldron.");
            Assert.That(contents, Has.All.EqualTo("CFElixirOfLifeMinor"));
            Assert.That(Solutions.TryGetSolution(cauldron, "cauldron", out _, out var left));
            Assert.That(left!.GetTotalPrototypeQuantity("Water").Float(), Is.EqualTo(20f), "A brew takes 10 units of water.");
        });
    }

    [Test]
    public async Task WithoutTheFormulaNothingBrews()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var cauldron = await CauldronOnAFire(30);
        EntityUid lily = default;
        await Server.WaitPost(() =>
        {
            var brewer = Brewer();
            PutIn(cauldron, "CFAirLily");
            lily = SEntMan.SpawnEntity("CFAirLily", _map.Local(0, 0));
            PutIn(cauldron, "CFAgaricMushroom");
            Assert.That(Hands.TryPickupAnyHand(brewer, lily, checkActionBlocker: false));
            Interaction.InteractUsing(brewer, lily, cauldron, SEntMan.GetComponent<TransformComponent>(cauldron).Coordinates, checkCanInteract: false, checkCanUse: false);
        });

        await Pair.RunTicksSync(Timing.TickRate * 30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Contents(cauldron).ConvertAll(Proto), Is.EquivalentTo(new[] { "CFAirLily", "CFAirLily", "CFAgaricMushroom" }), "Nothing is brewed without the formula, and nothing is lost.");
        });
    }

    [Test]
    public async Task DrinkingAPotionAppliesItsEffects()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        EntityUid elixir = default;
        await Server.WaitPost(() =>
        {
            hero = Brewer();
            elixir = SEntMan.SpawnEntity("CFCheetahsElixirLesser", _map.Local(0, 0));
            Assert.That(Hands.TryPickupAnyHand(hero, elixir, checkActionBlocker: false));
            Interaction.UseInHandInteraction(hero, elixir, checkCanUse: false, checkCanInteract: false);
        });

        await Pair.RunTicksSync(Timing.TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(elixir), "An elixir is drunk up.");
            Assert.That(SEntMan.System<CFBonusSystem>().TotalOfType(hero, CFBonusStat.Speed, CFBonusType.Status), Is.EqualTo(5), "Cheetah's Elixir: +5 feet of Speed.");
        });

        EntityUid escape = default;
        await Server.WaitPost(() =>
        {
            escape = SEntMan.SpawnEntity("CFPotionOfEmergencyEscape", _map.Local(0, 0));
            Assert.That(Hands.TryPickupAnyHand(hero, escape, checkActionBlocker: false));
            Interaction.UseInHandInteraction(hero, escape, checkCanUse: false, checkCanInteract: false);
        });

        await Pair.RunTicksSync(Timing.TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(escape));
            Assert.That(SEntMan.System<CFBonusSystem>().TotalOfType(hero, CFBonusStat.Speed, CFBonusType.Status), Is.EqualTo(40), "Status bonuses do not stack: the escape's +40 counts.");
            Assert.That(SEntMan.System<CFConditionSystem>().IsCalmed(hero), "Fleeing: no hostile actions for a minute.");
        });

        EntityUid darkvision = default;
        await Server.WaitPost(() =>
        {
            darkvision = SEntMan.SpawnEntity("CFDarkvisionElixirLesser", _map.Local(0, 0));
            Assert.That(Hands.TryPickupAnyHand(hero, darkvision, checkActionBlocker: false));
            Interaction.UseInHandInteraction(hero, darkvision, checkCanUse: false, checkCanInteract: false);
        });

        await Pair.RunTicksSync(Timing.TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(darkvision));
            Assert.That(SEntMan.HasComponent<NightVisionComponent>(hero), "Darkvision Elixir: the drinker sees in the dark.");
            Assert.That(SEntMan.GetComponent<CFConditionComponent>(hero).TimedDarkvision, "A human's darkvision from an elixir ends.");
        });
    }

    [Test]
    public async Task AThrownBombStrikesTheTargetAndSplashesItsNeighbour()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid thrower = default;
        EntityUid target = default;
        EntityUid neighbour = default;
        EntityUid bomb = default;
        int targetHp = 0;
        int neighbourHp = 0;
        await Server.WaitPost(() =>
        {
            thrower = Brewer(dexterity: Extreme);
            // Enemies with no mind, so they stand where the splash can be measured.
            target = SEntMan.SpawnEntity("CFAlchemyTestEnemy", _map.Local(3, 0));
            neighbour = SEntMan.SpawnEntity("CFAlchemyTestEnemy", _map.Local(4, 0));
            targetHp = SEntMan.GetComponent<CFCombatantComponent>(target).CurrentHp;
            neighbourHp = SEntMan.GetComponent<CFCombatantComponent>(neighbour).CurrentHp;
            bomb = SEntMan.SpawnEntity("CFAlchemistsFireLesser", _map.Local(0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(target);
            SEntMan.System<ThrowingSystem>().TryThrow(bomb, new Vector2(5f, 0f), user: thrower);
        });

        await Pair.RunTicksSync(Timing.TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(bomb), "A bomb breaks on its Strike.");
                Assert.That(SEntMan.GetComponent<CFCombatantComponent>(target).CurrentHp, Is.LessThan(targetHp - 1), "The target takes the fire and the splash.");
                Assert.That(SEntMan.GetComponent<CFCombatantComponent>(neighbour).CurrentHp, Is.LessThan(neighbourHp), "The creature beside it takes the splash.");
            });
        });
    }
}
