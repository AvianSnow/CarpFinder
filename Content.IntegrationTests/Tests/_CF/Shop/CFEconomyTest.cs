using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;
using Content.Client.CF.Character;
using Content.Client.CF.Shop;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Shop;
using Content.Client.UserInterface.Systems.Character.Windows;
using Content.Shared.CF.Shop;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Placeable;
using Content.Shared.Roles;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Shop;

/// <summary>
/// Coins in a wallet, a merchant buying what adventurers place on their counter, and selling to the Merchants'
/// Guild on its pallets, with goods really placed on them.
/// </summary>
[TestFixture]
[TestOf(typeof(CFMerchantSystem))]
[TestOf(typeof(CFCoinSystem))]
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

- type: entity
  parent: BaseItem
  id: CFEconomyTestWorthless
";

    private CFDevMap _map = default!;

    private CFCoinSystem Coins => SEntMan.System<CFCoinSystem>();
    private CFMerchantSystem Merchant => SEntMan.System<CFMerchantSystem>();

    /// <summary>A human holding a wallet with 10 gp in it.</summary>
    private EntityUid Human()
    {
        var mob = SEntMan.SpawnEntity("CFEconomyTestHuman", _map.Local(2, 0));
        var wallet = SEntMan.SpawnEntity("CFWallet", _map.Local(2, 0));
        Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(mob, wallet));
        Coins.PayInto(wallet, mob, 1000);
        return mob;
    }

    private EntityUid MerchantHuman()
    {
        var merchant = Human();
        foreach (var special in Server.ProtoMan.Index(new ProtoId<JobPrototype>("CFMerchant")).Special)
        {
            special.AfterEquip(merchant);
        }

        Assert.That(SEntMan.HasComponent<CFShopAccessComponent>(merchant), "The merchant job grants the shop.");
        return merchant;
    }

    private int Wallet(EntityUid mob) => Coins.Total(mob);

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
    public void WorthAndSharesFollowTheRules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFCoins.Worth(300, 1, 1), Is.EqualTo(300));
            Assert.That(CFCoins.Worth(100, 5, 10), Is.EqualTo(50), "Half a stack is half the Price.");
            Assert.That(CFCoins.Share(300, 50), Is.EqualTo(150));
            Assert.That(CFCoins.Share(5, 50), Is.EqualTo(2), "Fractions of a copper round down.");
            Assert.That(CFCoins.Breakdown(1234).Select(c => c.Count), Is.EqualTo(new[] { 1, 2, 3, 4 }),
                "1 pp, 2 gp, 3 sp and 4 cp.");
        });
    }

    [Test]
    public async Task TheWalletHoldsTheMoneyAndGivesChange()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = Human();
            Assert.That(Wallet(mob), Is.EqualTo(1000));

            Assert.That(Coins.TrySpend(mob, 250));
            Assert.That(Wallet(mob), Is.EqualTo(750), "A gold coin is broken and the change put back.");

            Assert.That(Coins.TrySpend(mob, 1000), Is.False);
            Assert.That(Wallet(mob), Is.EqualTo(750), "Nothing is taken when it cannot pay.");

            Coins.Pay(mob, 30);
            Assert.That(Wallet(mob), Is.EqualTo(780));
        });
    }

    [Test]
    public async Task UsingCoinsPutsThemInTheWallet()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = Human();
            var gold = SEntMan.SpawnEntity("CFGoldCoin", _map.Local(2, 0));
            var interaction = SEntMan.System<SharedInteractionSystem>();

            Assert.That(interaction.UseInHandInteraction(mob, gold));
            Assert.That(Wallet(mob), Is.EqualTo(2000), "Ten gold coins went in.");
        });
    }

    private List<EntityUid> Placed(EntityUid counter)
    {
        return Merchant.Placed(counter);
    }

    /// <summary>The coins lying on the counter, in copper.</summary>
    private int CoinsOn(EntityUid counter)
    {
        var total = 0;
        foreach (var uid in SEntMan.GetComponent<ItemPlacerComponent>(counter).PlacedEntities)
        {
            total += SEntMan.System<CFWalletSystem>().CoinValue(uid);
        }

        return total;
    }

    [Test]
    public async Task TheMerchantBuysAndLaysTheCoinsOnTheCounter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFThievesToolkit");
        await Server.WaitAssertion(() =>
        {
            var adventurer = Human();
            var merchant = MerchantHuman();
            var toolkit = Placed(counter).Single();

            Assert.That(Merchant.TryBuy(counter, adventurer, toolkit), Is.False, "Only the merchant buys.");
            Assert.That(Merchant.TryBuy(counter, merchant, toolkit));
            Assert.Multiple(() =>
            {
                Assert.That(Wallet(merchant), Is.EqualTo(1000 - 300), "The market price, 3 gp.");
                Assert.That(Wallet(adventurer), Is.EqualTo(1000), "Nothing goes to anyone's wallet.");
                Assert.That(Merchant.Stored(counter), Is.EqualTo(new[] { toolkit }), "It waits in the store.");
                Assert.That(Placed(counter), Is.Empty, "Coins on the counter are not goods.");
            });
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
            Assert.That(CoinsOn(counter), Is.EqualTo(300), "The coins lie on the counter to be picked up."));
    }

    [Test]
    public async Task TheMerchantPaysThePriceTheyType()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFThievesToolkit");
        await Server.WaitAssertion(() =>
        {
            var merchant = MerchantHuman();
            var toolkit = Placed(counter).Single();

            Assert.That(Merchant.TryBuy(counter, merchant, toolkit, 210), "2 gp 1 sp for a 3 gp toolkit.");
            Assert.That(Wallet(merchant), Is.EqualTo(1000 - 210));
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() => Assert.That(CoinsOn(counter), Is.EqualTo(210)));
    }

    [Test]
    public async Task TheMerchantCannotBuyWhatTheyCannotAfford()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFGemTopaz");
        await Server.WaitAssertion(() =>
        {
            var merchant = MerchantHuman();
            var gem = Placed(counter).Single();

            Assert.That(Merchant.TryBuy(counter, merchant, gem), Is.False, "125 gp from a 10 gp wallet.");
            Assert.That(Wallet(merchant), Is.EqualTo(1000));
            Assert.That(Placed(counter), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task StoredGoodsAreTakenOrSoldToTheGuildAtFullWorth()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFThievesToolkit", "CFCrowbar");
        await Server.WaitAssertion(() =>
        {
            var merchant = MerchantHuman();
            Coins.Pay(merchant, 1000);
            foreach (var item in Placed(counter))
            {
                Assert.That(Merchant.TryBuy(counter, merchant, item));
            }

            Assert.That(Wallet(merchant), Is.EqualTo(2000 - 300 - 50));
            Assert.That(Merchant.Stored(counter), Has.Count.EqualTo(2));

            var adventurer = Human();
            var crowbar = Merchant.Stored(counter).Single(uid =>
                SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CFCrowbar");
            Assert.That(Merchant.TryTake(counter, adventurer, crowbar), Is.False);
            Assert.That(Merchant.TryTake(counter, merchant, crowbar));
            Assert.That(Merchant.Stored(counter), Has.Count.EqualTo(1));

            Assert.That(Merchant.SellStored(counter, adventurer, null), Is.Zero);
            Assert.That(Merchant.SellStored(counter, merchant, null), Is.EqualTo(300), "No guild cut for a member.");
            Assert.That(Wallet(merchant), Is.EqualTo(2000 - 300 - 50 + 300));
            Assert.That(Merchant.Stored(counter), Is.Empty);
        });
    }

    [Test]
    public async Task OnlyAMerchantOpensTheCounter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter();
        await Server.WaitAssertion(() =>
        {
            bool Opens(EntityUid user)
            {
                var attempt = new ActivatableUIOpenAttemptEvent(user, true);
                SEntMan.EventBus.RaiseLocalEvent(counter.Owner, attempt);
                return !attempt.Cancelled;
            }

            Assert.That(Opens(Human()), Is.False);
            Assert.That(Opens(MerchantHuman()));
        });
    }

    [Test]
    public async Task TheGuildKeepsHalfExceptFromMerchants()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var palletUid = SEntMan.SpawnEntity("CFWoodenPalletSell", _map.Local(0, 0));
            var pallet = new Entity<CFGuildPalletComponent>(palletUid, SEntMan.GetComponent<CFGuildPalletComponent>(palletUid));
            var gem = SEntMan.SpawnEntity("CFGemTopaz", _map.Local(0, 0));

            var adventurer = Human();
            Assert.That(Merchant.SellToGuild(pallet, adventurer), Is.EqualTo(6250), "Half, even for a gem.");
            Assert.That(SEntMan.Deleted(gem));
            Assert.That(Wallet(adventurer), Is.EqualTo(1000 + 6250));

            SEntMan.SpawnEntity("CFGemTopaz", _map.Local(0, 0));
            var merchant = MerchantHuman();
            Assert.That(Merchant.SellToGuild(pallet, merchant), Is.EqualTo(12500), "Members keep it all.");
        });
    }

    [Test]
    public async Task WorthlessGoodsStayPut()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var counter = await Counter("CFEconomyTestWorthless");
        await Server.WaitAssertion(() =>
        {
            var merchant = MerchantHuman();
            var junk = Placed(counter).Single();

            Assert.That(Merchant.TryBuy(counter, merchant, junk), Is.False);
            Assert.That(Wallet(merchant), Is.EqualTo(1000));
            Assert.That(Placed(counter), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task OnlyMerchantsCanBuyAndOrdersLandOnTheBuyPallet()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var order = new CFShopPurchaseEvent();
            order.Lines.Add(new CFShopPurchaseLine { Prototype = "CFCrowbar", Quantity = 1 });

            var adventurer = Human();
            SEntMan.System<CFShopSystem>().Purchase(adventurer, order);
            Assert.That(Wallet(adventurer), Is.EqualTo(1000), "Only merchants have the shop.");

            var pallet = SEntMan.SpawnEntity("CFWoodenPalletBuy", _map.Local(3, 0));
            var merchant = MerchantHuman();
            SEntMan.System<CFShopSystem>().Purchase(merchant, order);
            Assert.That(Wallet(merchant), Is.EqualTo(1000 - 50));

            var onPallet = SEntMan.System<EntityLookupSystem>()
                .GetEntitiesInRange(SEntMan.GetComponent<TransformComponent>(pallet).Coordinates, 0.3f)
                .Any(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CFCrowbar");
            Assert.That(onPallet, "The order arrived on the buy pallet beside the merchant.");
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
            window.SetMerchant(true);
            var entries = new List<CFMerchantEntry>
            {
                new("crowbar", 50),
                new("rock", 0),
            };
            window.UpdateState(new CFMerchantState(entries, CFMerchantMode.GuildSell, 50));
            window.UpdateState(new CFMerchantState(entries, CFMerchantMode.GuildBuy, 50));

            var offers = new List<CFCounterEntry>
            {
                new(new NetEntity(1), "CFCrowbar", "crowbar", "A bar.", 1234),
                new(new NetEntity(2), "", "rock", "", 0),
            };
            var state = new CFMerchantCounterState(offers, offers);
            new CFCounterWindow().UpdateState(state, 1000);
            new CFCounterStoreWindow().UpdateState(state);
            _ = new CFShopWindow();
        });
    }
}
