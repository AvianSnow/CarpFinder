using System.Collections.Generic;
using Content.Client.CF.Character;
using Content.Client.CF.Shop;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Shop;
using Content.Client.UserInterface.Systems.Character.Windows;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Shop;
using Content.Shared.Placeable;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Shop;

/// <summary>Coins, the purse, and selling at a merchant's counter, with goods really placed on it.</summary>
[TestFixture]
[TestOf(typeof(CFMerchantSystem))]
public sealed class CFEconomyTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFEconomyTestHuman
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
  - type: CFPurse
    seeded: true
    copper: 1000

- type: entity
  parent: BaseItem
  id: CFEconomyTestWorthless
";

    private CFDevMap _map = default!;

    private CFCoinSystem Coins => SEntMan.System<CFCoinSystem>();
    private CFMerchantSystem Merchant => SEntMan.System<CFMerchantSystem>();

    private EntityUid Human() => SEntMan.SpawnEntity("CFEconomyTestHuman", _map.Local(2, 0));

    private int Purse(EntityUid mob) => SEntMan.GetComponent<CFPurseComponent>(mob).Copper;

    private async Task<Entity<CFMerchantCounterComponent>> Counter(params string[] goods)
    {
        Entity<CFMerchantCounterComponent> counter = default;
        var items = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity("CFMerchantCounter", _map.Local(0, 0));
            counter = (uid, SEntMan.GetComponent<CFMerchantCounterComponent>(uid));
            foreach (var good in goods)
            {
                var item = SEntMan.SpawnEntity(good, _map.Local(0, 0));
                SEntMan.System<SharedPhysicsSystem>().WakeBody(item);
                items.Add(item);
            }
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(counter).PlacedEntities, Is.SupersetOf(items)));
        return counter;
    }

    [Test]
    public void SaleValuesFollowTheRules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFCoins.SaleValue(300, false, 1, 1), Is.EqualTo(150), "Most items sell for half.");
            Assert.That(CFCoins.SaleValue(25000, true, 1, 1), Is.EqualTo(25000), "Gems sell in full.");
            Assert.That(CFCoins.SaleValue(100, false, 5, 10), Is.EqualTo(25), "Half a stack is half the Price.");
            Assert.That(CFCoins.SaleValue(5, false, 1, 1), Is.EqualTo(2), "Fractions of a copper round down.");
        });
    }

    [Test]
    public async Task PocketingAddsTheWholeStack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = Human();
            var uid = SEntMan.SpawnEntity("CFGoldCoin", _map.Local(2, 0));

            var value = Coins.Pocket((uid, SEntMan.GetComponent<CFCoinComponent>(uid)), mob);

            Assert.That(value, Is.EqualTo(1000));
            Assert.That(Purse(mob), Is.EqualTo(2000));
            Assert.That(SEntMan.Deleted(uid));
        });
    }

    [Test]
    public async Task TakingCoinsOutMovesExactValueAndCannotOverdraw()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = Human();

            var coins = Coins.TakeOut(mob, "CFGoldCoin", CFCoins.CopperPerGold, 3);
            Assert.That(coins, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<StackComponent>(coins!.Value).Count, Is.EqualTo(3));
            Assert.That(Purse(mob), Is.EqualTo(700));

            Assert.That(Coins.TakeOut(mob, "CFPlatinumCoin", CFCoins.CopperPerPlatinum, 1), Is.Null);
            Assert.That(Purse(mob), Is.EqualTo(700));

            Coins.Pocket((coins.Value, SEntMan.GetComponent<CFCoinComponent>(coins.Value)), mob);
            Assert.That(Purse(mob), Is.EqualTo(1000), "Coins out and back in lose nothing.");
        });
    }

    [Test]
    public async Task SellingPaysHalfForGoodsAndFullForGems()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFThievesToolkit", "CFGemTopaz");
        await Server.WaitAssertion(() =>
        {
            var mob = Human();

            Assert.That(Merchant.Sell(counter, mob), Is.EqualTo(150 + 12500));
            Assert.That(Purse(mob), Is.EqualTo(1000 + 150 + 12500));
            Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(counter).PlacedEntities, Is.Empty);
        });
    }

    [Test]
    public async Task WorthlessGoodsStayOnTheCounter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFEconomyTestWorthless");
        await Server.WaitAssertion(() =>
        {
            var mob = Human();

            Assert.That(Merchant.Sell(counter, mob), Is.Zero);
            Assert.That(Purse(mob), Is.EqualTo(1000));
            Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(counter).PlacedEntities, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task BuyingAndResellingLosesMoney()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var item = SEntMan.SpawnEntity("CFCrowbar", _map.Local(2, 0));
            Assert.That(Merchant.SaleValue(item), Is.EqualTo(25), "A 5 sp crowbar fetches half.");
        });
    }

    [Test]
    public async Task OnlyMerchantsCanBuy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var order = new CFShopPurchaseEvent();
            order.Lines.Add(new CFShopPurchaseLine { Prototype = "CFCrowbar", Quantity = 1 });

            var adventurer = Human();
            SEntMan.System<CFShopSystem>().Purchase(adventurer, order);
            Assert.That(Purse(adventurer), Is.EqualTo(1000), "Only merchants have the shop.");

            var merchant = Human();
            foreach (var special in Server.ProtoMan.Index<JobPrototype>("CFMerchant").Special)
            {
                special.AfterEquip(merchant);
            }

            Assert.That(SEntMan.HasComponent<CFShopAccessComponent>(merchant), "The merchant job grants the shop.");
            SEntMan.System<CFShopSystem>().Purchase(merchant, order);
            Assert.That(Purse(merchant), Is.EqualTo(1000 - 50));
        });
    }

    [Test]
    public async Task ShopTabFollowsAccess()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new CharacterWindow();
            window.Tabs.CurrentTab = 3;

            window.SetShopVisible(false);
            Assert.That(window.Tabs.GetTabVisible(3), Is.False);
            Assert.That(window.Tabs.CurrentTab, Is.Zero, "It does not stay on a hidden tab.");

            window.SetShopVisible(true);
            Assert.That(window.Tabs.GetTabVisible(3));
        });
    }

    [Test]
    public async Task WindowsLoad()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new CFMerchantWindow();
            window.UpdateState(new CFMerchantState(new List<CFMerchantEntry>
            {
                new("crowbar", 25),
                new("rock", 0),
            }, 25));
            _ = new CFShopWindow();
        });
    }
}
