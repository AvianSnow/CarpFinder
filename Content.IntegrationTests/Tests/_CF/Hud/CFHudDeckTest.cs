using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Hud;

/// <summary>What the HUD deck needs from the game: prepared charges, the basic actions, and drawing carried items.</summary>
[TestFixture]
[TestOf(typeof(CFQuickItemSystem))]
public sealed class CFHudDeckTest : GameTest
{
    private const string Potion = "CFHealingPotionMinor";
    private const string Bag = "ClothingBackpack";

    private static readonly string[] Basic =
    {
        "CFActManeuver", "CFActGrapple", "CFActTumble", "CFActDemoralize", "CFActFeint", "CFActAid", "CFActRecall", "CFActHide", "CFActSeek",
    };

    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private CFDevMap _map = default!;

    private EntityUid Built(CFCharacterBuildData build)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
        return hero;
    }

    private static CFCharacterBuildData Wizard(params string[] rankOne)
    {
        var build = new CFCharacterBuildData { Class = "Wizard" };
        for (var i = 0; i < rankOne.Length; i++)
            build.Spells.Add(new CFSpellPick { Rank = 1, Index = i, Spell = rankOne[i] });

        return build;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private void CastBless(EntityUid hero)
    {
        SEntMan.System<SharedActionsSystem>().PerformAction(hero, Action(hero, "CFBless"), new CFSpellCastInstantEvent());
    }

    [Test]
    public async Task APreparedCasterStartsWithItsLobbyPicksAndSpendsThem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Built(Wizard("Bless", "Bless"));
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            var spells = SEntMan.System<CFSpellSystem>();
            var spell = SEntMan.GetComponent<CFSpellComponent>(Action(hero, "CFBless"));

            Assert.That(book.Prepared);
            Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((2, 2)));

            CastBless(hero);
            Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((1, 2)));
            CastBless(hero);
            Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((0, 2)));
            Assert.That(spells.HasSlot(hero, spell), Is.False);

            var expended = book.Expended[1];
            CastBless(hero);
            Assert.That(book.Expended[1], Is.EqualTo(expended), "An empty spell costs nothing and does nothing.");
        });
    }

    [Test]
    public async Task ASpontaneousCasterStillSpendsAnySlotOfTheRank()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData { Class = "Bard" };
            build.Spells.Add(new CFSpellPick { Rank = 1, Index = 0, Spell = "Bless" });
            var hero = Built(build);
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);

            Assert.That(book.Prepared, Is.False);
            CastBless(hero);
            Assert.That(book.Expended[1], Is.EqualTo(1));
            Assert.That(book.PreparedSlots, Is.Empty);
        });
    }

    [Test]
    public async Task APlanTakesEffectAtTheLongRestAndNotBefore()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Built(Wizard("Bless", "Charm"));
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            var plan = new List<CFPreparedSlot> { new("Charm", 1), new("Charm", 1) };

            Assert.That(SEntMan.System<CFSpellPreparationSystem>().TrySetPlan((hero, book), plan));
            Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((1, 1)), "Nothing changes yet.");

            SEntMan.System<CFLongRestSystem>().LongRest(hero);
            Assert.Multiple(() =>
            {
                Assert.That(CFPreparedSpells.Charges(book, "Charm"), Is.EqualTo((2, 2)));
                Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((0, 0)));
            });
        });
    }

    [Test]
    public async Task ABadPlanFromAClientIsRefused()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Built(Wizard("Bless"));
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            var planned = book.PlannedSlots.Count;
            var plan = new List<CFPreparedSlot> { new("Fireball", 1) };

            Assert.That(SEntMan.System<CFSpellPreparationSystem>().TrySetPlan((hero, book), plan), Is.False);
            Assert.That(book.PlannedSlots, Has.Count.EqualTo(planned));
        });
    }

    [Test]
    public async Task EveryoneGetsTheBasicActionsOnTheCommonTabOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Built(new CFCharacterBuildData { Class = "Fighter" });
            foreach (var id in Basic)
            {
                var action = Action(hero, id);
                var slot = SEntMan.GetComponent<CFRadialSlotComponent>(action);
                Assert.Multiple(() =>
                {
                    Assert.That(slot.Tab, Is.EqualTo(CFDeckSection.Common), id);
                    Assert.That(slot.Wheel, Is.False, id);
                    Assert.That(slot.Group, Is.EqualTo(CFRadialGroup.Free), $"{id} pays through CFManeuverSystem, not its slot.");
                    Assert.That(action.Comp.AutoPopulate, Is.False, id);
                    Assert.That(SEntMan.HasComponent<CFBasicActionComponent>(action), id);
                });
            }
        });
    }

    /// <summary>Only a client resolves sprites, so this is what catches a bad icon path.</summary>
    [Test]
    public async Task TheBasicActionIconsLoad()
    {
        await Client.WaitAssertion(() =>
        {
            foreach (var id in Basic)
            {
                var action = CEntMan.SpawnEntity(id, MapCoordinates.Nullspace);
                Assert.That(CEntMan.HasComponent<SpriteComponent>(action), id);
                CEntMan.DeleteEntity(action);
            }
        });
    }

    private EntityUid Carrier(out EntityUid bag)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var inventory = SEntMan.System<InventorySystem>();
        bag = SEntMan.SpawnEntity(Bag, _map.Local(0, 0));
        Assert.That(inventory.TryEquip(hero, bag, "back", silent: true, force: true));
        return hero;
    }

    private void IntoBag(EntityUid bag, EntityUid item)
    {
        var storage = SEntMan.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
        Assert.That(storage.Insert(bag, item, out _, playSound: false));
    }

    [Test]
    public async Task ItemsInABagCanBeDrawnToAFreeHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Carrier(out var bag);
            var potion = SEntMan.SpawnEntity(Potion, _map.Local(0, 0));
            IntoBag(bag, potion);

            var quick = SEntMan.System<CFQuickItemSystem>();
            var found = new List<EntityUid>();
            quick.Carried(hero, found);
            Assert.That(found, Is.EqualTo(new[] { potion }));

            Assert.That(quick.TryDraw(hero, potion));
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(hero, potion));

            quick.Carried(hero, found);
            Assert.That(found, Is.Empty, "What is in hand is not listed.");
        });
    }

    [Test]
    public async Task AnItemInAPocketCanBeDrawn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            var inventory = SEntMan.System<InventorySystem>();
            var suit = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", _map.Local(0, 0));
            var potion = SEntMan.SpawnEntity(Potion, _map.Local(0, 0));
            Assert.That(inventory.TryEquip(hero, suit, "jumpsuit", silent: true, force: true));
            Assert.That(inventory.TryEquip(hero, potion, "pocket1", silent: true, force: true));

            Assert.That(SEntMan.System<CFQuickItemSystem>().TryDraw(hero, potion));
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(hero, potion));
        });
    }

    [Test]
    public async Task NoFreeHandNoDraw()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Carrier(out var bag);
            var hands = SEntMan.System<SharedHandsSystem>();
            while (hands.CountFreeHands(hero) > 0)
                Assert.That(hands.TryPickupAnyHand(hero, SEntMan.SpawnEntity(Potion, _map.Local(0, 0))));

            var potion = SEntMan.SpawnEntity(Potion, _map.Local(0, 0));
            IntoBag(bag, potion);
            Assert.That(SEntMan.System<CFQuickItemSystem>().TryDraw(hero, potion), Is.False);
            Assert.That(hands.IsHolding(hero, potion), Is.False);
        });
    }

    [Test]
    public async Task SomeoneElsesItemCannotBeDrawn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Carrier(out _);
            var other = Carrier(out var theirBag);
            var potion = SEntMan.SpawnEntity(Potion, _map.Local(0, 0));
            IntoBag(theirBag, potion);

            Assert.That(SEntMan.System<CFQuickItemSystem>().TryDraw(hero, potion), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(other, potion), Is.False);
        });
    }

    /// <summary>The Items tab reads the client's copy of a closed bag, so the contents must have been sent.</summary>
    [Test]
    public async Task TheClientSeesWhatIsInsideAClosedBag()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, potion = default;
        await Server.WaitPost(() =>
        {
            hero = Carrier(out var bag);
            potion = SEntMan.SpawnEntity(Potion, _map.Local(0, 0));
            IntoBag(bag, potion);
        });
        await Pair.RunTicksSync(5);

        var clientHero = CEntMan.GetEntity(SEntMan.GetNetEntity(hero));
        var clientPotion = CEntMan.GetEntity(SEntMan.GetNetEntity(potion));
        await Client.WaitAssertion(() =>
        {
            var found = new List<EntityUid>();
            CEntMan.System<CFQuickItemSystem>().Carried(clientHero, found);
            Assert.That(found, Is.EqualTo(new[] { clientPotion }));
        });
    }
}
