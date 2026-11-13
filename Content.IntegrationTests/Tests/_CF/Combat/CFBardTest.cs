using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Courageous Anthem as an aura, auras as circles, and the Bard's instrument.</summary>
[TestFixture]
[TestOf(typeof(CFAuraSystem))]
public sealed class CFBardTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFBardTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Hero(int x = 0, int y = 0)
    {
        return SEntMan.SpawnEntity("CFBardTestHero", _map.Local(x, y));
    }

    private void Place(EntityUid mob, int x, int y, float nudge = 0f)
    {
        SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, _map.Local(x, y).Offset(new Vector2(nudge, 0f)));
    }

    [Test]
    public async Task TheAnthemLastsAMinuteThenEnds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid bard = default, ally = default;
        int during = 0, shown = 0, after = 0;
        TimeSpan left = default;
        await Server.WaitPost(() =>
        {
            bard = Hero();
            ally = Hero(5);
            SEntMan.System<CFActivitySystem>().Anthem(bard);
            during = Bonuses.Total(ally, CFBonusStat.AttackRoll);
            shown = Bonuses.TimedTotal(ally, CFBonusStat.AttackRoll);
            left = SEntMan.GetComponent<CFAuraComponent>(bard).AnthemUntil - Server.ResolveDependency<IGameTiming>().CurTime;
            SEntMan.GetComponent<CFAuraComponent>(bard).AnthemUntil = TimeSpan.Zero;
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => after = Bonuses.Total(ally, CFBonusStat.AttackRoll));

        Assert.Multiple(() =>
        {
            Assert.That(during, Is.EqualTo(1));
            Assert.That(shown, Is.EqualTo(1), "The HUD's status row counts an aura's bonus.");
            Assert.That(left, Is.EqualTo(CFActivitySystem.AnthemLasts));
            Assert.That(after, Is.Zero, "Once it runs out, the bonus comes off.");
        });
    }

    [Test]
    public async Task UsingTheAnthemAgainDoesNotStackOrShortenIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var bard = Hero();
            var activities = SEntMan.System<CFActivitySystem>();
            activities.Anthem(bard);
            var until = SEntMan.GetComponent<CFAuraComponent>(bard).AnthemUntil;
            activities.Anthem(bard, TimeSpan.FromSeconds(2));

            Assert.That(SEntMan.GetComponent<CFAuraComponent>(bard).AnthemUntil, Is.EqualTo(until));
            Assert.That(Bonuses.Total(bard, CFBonusStat.AttackRoll), Is.EqualTo(1));
            Assert.That(Bonuses.Total(bard, CFBonusStat.Damage), Is.EqualTo(1));
        });
    }

    /// <summary>The aura counts a circle around where its owner really stands, which is the ring the client draws.</summary>
    [Test]
    public async Task AnAuraIsACircleAroundItsOwner()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid ally = default;
        int near = 0, far = 0, diagonal = 0, tooFarDiagonal = 0;
        await Server.WaitPost(() =>
        {
            var owner = Hero();
            ally = Hero(3);
            SEntMan.System<CFAuraSystem>().Add(owner, new[]
            {
                new CFAuraEffect
                {
                    Affects = CFAuraAffects.Allies,
                    Stat = CFBonusStat.AttackRoll,
                    Type = CFBonusType.Status,
                    Value = 1,
                },
            }, 15);
            Place(ally, 3, 0, 0.4f);
        });

        // 15 feet reaches 3.5 tiles from the owner's centre: 3 tiles, and half of the owner's own.
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            near = Bonuses.Total(ally, CFBonusStat.AttackRoll);
            Place(ally, 3, 0, 0.6f);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            far = Bonuses.Total(ally, CFBonusStat.AttackRoll);
            Place(ally, 2, 2);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            diagonal = Bonuses.Total(ally, CFBonusStat.AttackRoll);
            Place(ally, 3, 2);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => tooFarDiagonal = Bonuses.Total(ally, CFBonusStat.AttackRoll));

        Assert.Multiple(() =>
        {
            Assert.That(near, Is.EqualTo(1), "3.4 tiles away is inside.");
            Assert.That(far, Is.Zero, "3.6 tiles away is outside.");
            Assert.That(diagonal, Is.EqualTo(1), "Two diagonal steps is 15 feet.");
            Assert.That(tooFarDiagonal, Is.Zero, "Three across and two up is further than 15 feet.");
        });
    }

    [Test]
    public async Task ABardStartsWithAVirtuosoLute()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var bard = Hero();
            var before = Lutes();
            var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Bard")).Features.First(f => f.Name == "Composition Spells");
            SEntMan.System<CFCharacterFeatureSystem>().Apply(bard, feature.Grants, "feature:compositions");

            Assert.That(Lutes() - before, Is.EqualTo(1), "Other tests on this server may have left lutes about.");
            Assert.That(SEntMan.GetComponent<CFCombatOptionsComponent>(bard).Anthem);
        });
    }

    private int Lutes()
    {
        var lutes = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == "CFVirtuosoLute")
                lutes++;
        }

        return lutes;
    }

    [Test]
    [TestCase("CFVirtuosoLute")]
    [TestCase("CFLuteInstrument")]
    [TestCase("CFFluteInstrument")]
    public async Task UsingAnInstrumentInHandKeepsIt(string id)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid bard = default, instrument = default;
        await Server.WaitPost(() =>
        {
            bard = Hero();
            SEntMan.GetComponent<CFCombatOptionsComponent>(bard).Anthem = true;
            instrument = SEntMan.SpawnEntity(id, _map.Local(0, 0));
            SEntMan.System<SharedHandsSystem>().PickupOrDrop(bard, instrument, checkActionBlocker: false, animate: false);
            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(bard, instrument);
        });
        await Pair.RunSeconds(1f);
        await Server.WaitPost(() => SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(bard, instrument));
        await Pair.RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(instrument), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(bard, instrument));
        });
    }
}

/// <summary>
/// An instrument plays on after its window closes. The test client has no MIDI, so the song is started on the server
/// as the client's start message would. The client side of it, that the window no longer stops the song as it closes,
/// is the marked edit in <c>InstrumentMenu.ExitedTree</c>.
/// </summary>
[TestFixture]
[TestOf(typeof(Content.Server.CF.Instruments.CFInstrumentSystem))]
public sealed class CFInstrumentKeepPlayingTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);
    private static readonly Content.Shared.Instruments.InstrumentUiKey Key = Content.Shared.Instruments.InstrumentUiKey.Key;

    public override PoolSettings PoolSettings => new() { Connected = false };

    private void SetPlaying(EntityUid instrument)
    {
        // What the client's start message does. The setter is the instrument system's own.
        var comp = SEntMan.GetComponent<Content.Server.Instruments.InstrumentComponent>(instrument);
        typeof(Content.Shared.Instruments.SharedInstrumentComponent).GetProperty("Playing")!.SetValue(comp, true);
    }

    private bool Playing(EntityUid instrument)
    {
        return SEntMan.GetComponent<Content.Server.Instruments.InstrumentComponent>(instrument).Playing;
    }

    [Test]
    [TestCase("CFVirtuosoLute")]
    [TestCase("CFFluteInstrument")]
    [TestCase("CFLuteInstrument")]
    public async Task AHeldInstrumentPlaysOnUntilLetGo(string id)
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, instrument = default;
        var ui = SEntMan.System<Robust.Server.GameObjects.UserInterfaceSystem>();
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
            instrument = SEntMan.SpawnEntity(id, map.Local(0, 0));
            SEntMan.System<SharedHandsSystem>().PickupOrDrop(hero, instrument, checkActionBlocker: false, animate: false);
            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(hero, instrument);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitPost(() =>
        {
            Assert.That(ui.IsUiOpen(instrument, Key, hero), "Using it in hand opens the window.");
            SetPlaying(instrument);
            ui.CloseUi(instrument, Key, hero);
        });
        await Pair.RunTicksSync(30);

        bool afterClose = false, afterSwap = false, afterReopen = false, afterDrop = true;
        EntityUid? player = null;
        await Server.WaitPost(() =>
        {
            afterClose = Playing(instrument);
            player = SEntMan.System<Content.Server.Instruments.InstrumentSystem>().GetInstrumentPlayer(instrument);

            var hands = SEntMan.System<SharedHandsSystem>();
            var other = hands.EnumerateHands(hero).First(hand => !hands.TryGetHeldItem(hero, hand, out _));
            hands.TrySetActiveHand(hero, other);
        });
        await Pair.RunTicksSync(30);
        await Server.WaitPost(() =>
        {
            afterSwap = Playing(instrument);
            SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(hero, instrument);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitPost(() =>
        {
            afterReopen = Playing(instrument);
            ui.CloseUi(instrument, Key, hero);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => SEntMan.System<SharedHandsSystem>().TryDrop(hero, instrument, checkActionBlocker: false));
        await Pair.RunTicksSync(30);
        await Server.WaitPost(() => afterDrop = Playing(instrument));

        Assert.Multiple(() =>
        {
            Assert.That(afterClose, "Closing the window leaves it playing.");
            Assert.That(player, Is.EqualTo(hero), "The server still knows who plays it.");
            Assert.That(afterSwap, "Changing hands leaves it playing.");
            Assert.That(afterReopen, "Opening the window again leaves it playing.");
            Assert.That(afterDrop, Is.False, "Letting go stops it.");
        });
    }

    [Test]
    public async Task APlacedInstrumentPlaysOnWhileThePlayerStaysInReach()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, piano = default;
        var ui = SEntMan.System<Robust.Server.GameObjects.UserInterfaceSystem>();
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
            piano = SEntMan.SpawnEntity("PianoInstrument", map.Local(1, 0));
            SEntMan.System<SharedInteractionSystem>().InteractionActivate(hero, piano);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitPost(() =>
        {
            Assert.That(ui.IsUiOpen(piano, Key, hero), "Using the piano opens its window.");
            SetPlaying(piano);
            ui.CloseUi(piano, Key, hero);
        });
        await Pair.RunTicksSync(30);

        bool nearby = false, walkedOff = true;
        await Server.WaitPost(() =>
        {
            nearby = Playing(piano);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, map.Local(6, 0));
        });
        await Pair.RunTicksSync(30);
        await Server.WaitPost(() => walkedOff = Playing(piano));

        Assert.Multiple(() =>
        {
            Assert.That(nearby, "Standing at it, the song plays on.");
            Assert.That(walkedOff, Is.False, "Walking away stops it.");
        });
    }
}
