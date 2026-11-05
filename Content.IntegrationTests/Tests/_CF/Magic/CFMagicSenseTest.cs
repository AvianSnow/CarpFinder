using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Magic;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Magic;

/// <summary>Light, Detect Magic and Read Aura, including casts through the real spell actions.</summary>
[TestFixture]
[TestOf(typeof(CFMagicSenseSystem))]
public sealed class CFMagicSenseTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 12, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFMagicTestCaster
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private CFMagicSenseSystem Magic => SEntMan.System<CFMagicSenseSystem>();

    private EntityUid Caster() => SEntMan.SpawnEntity("CFMagicTestCaster", _map.Local(0, 0));

    private EntityUid Item(string id, int x) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    [Test]
    public async Task MagicalThingsAreKnownByTraitAndRunes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Magic.IsMagical(Item("CFEverburningTorch", 1)));
            Assert.That(Magic.IsMagical(Item("CFHoodedLantern", 1)), Is.False, "A lantern is only oil and glass.");
            Assert.That(Magic.IsMagical(Item("CFHealingPotionMinor", 1)), "Potions carry the magical tag.");

            var sword = Item("CFLongsword", 1);
            Assert.That(Magic.IsMagical(sword), Is.False);
            SEntMan.EnsureComponent<CFWeaponRunesComponent>(sword).Potency = 1;
            Assert.That(Magic.IsMagical(sword), "A potency rune is magic.");
        });
    }

    [Test]
    public async Task PulseReaches30FeetAndFindsTheStrongest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var user = Caster();
            Assert.That(Magic.Pulse(user).Count, Is.Zero);

            Item("CFEverburningTorch", 9);
            Assert.That(Magic.Pulse(user).Count, Is.Zero, "45 feet is beyond the pulse.");

            var torch = Item("CFEverburningTorch", 4);
            var pulse = Magic.Pulse(user);
            Assert.That(pulse.Count, Is.EqualTo(1));
            Assert.That(pulse.Strongest, Is.EqualTo(torch));
            Assert.That(pulse.Level, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PulseIgnoresWhatYouCarry()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var user = Caster();
            var torch = Item("CFEverburningTorch", 0);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, torch, checkActionBlocker: false));
            Assert.That(Magic.Pulse(user).Count, Is.Zero);
        });
    }

    [Test]
    public async Task LightFollowsItsTargetAndHeightens()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var user = Caster();
            var orb = Magic.CastLight(user, user, 1);
            Assert.That(orb, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<TransformComponent>(orb!.Value).ParentUid, Is.EqualTo(user));

            var lights = SEntMan.System<SharedPointLightSystem>();
            Assert.That(lights.TryGetLight(orb.Value, out var light));
            Assert.That(light!.Radius, Is.EqualTo(CFMagicSenseSystem.OrbRadius));

            var strong = Magic.CastLight(user, Item("CFHoodedLantern", 1), 4);
            Assert.That(lights.TryGetLight(strong!.Value, out var bright));
            Assert.That(bright!.Radius, Is.EqualTo(CFMagicSenseSystem.HeightenedOrbRadius));
        });
    }

    [Test]
    public async Task OnlyFourLightsAndRecastDismisses()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var user = Caster();
            var orbs = new List<EntityUid>();
            for (var i = 0; i < 4; i++)
            {
                orbs.Add(Magic.CastLight(user, Item("CFHoodedLantern", i + 1), 1)!.Value);
            }

            var target = Item("CFHoodedLantern", 6);
            var fifth = Magic.CastLight(user, target, 1);
            var live = 0;
            foreach (var orb in orbs)
            {
                if (!SEntMan.Deleted(orb))
                    live++;
            }

            Assert.That(live, Is.EqualTo(3), "A fifth light ends one of the four.");

            Assert.That(Magic.CastLight(user, target, 1), Is.Null);
            Assert.That(SEntMan.Deleted(fifth!.Value), "Casting on your own light dismisses it.");
        });
    }

    [Test]
    public async Task RealSpellActionsCastThem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var user = Caster();
            var effects = SEntMan.System<CFEffectSystem>();
            var lantern = Item("CFHoodedLantern", 1);

            effects.DeliverToTarget(SEntMan.SpawnEntity("CFLight", _map.Local(0, 0)), user, lantern);

            var found = false;
            var query = SEntMan.EntityQueryEnumerator<CFLightOrbComponent, TransformComponent>();
            while (query.MoveNext(out var orb, out var xform))
            {
                found |= orb.Caster == user && xform.ParentUid == lantern;
            }

            Assert.That(found, "The Light action should attach an orb to an object.");

            Assert.DoesNotThrow(() => effects.DeliverToTarget(SEntMan.SpawnEntity("CFReadAura", _map.Local(0, 0)), user, lantern));
            Assert.DoesNotThrow(() => effects.DeliverToTarget(SEntMan.SpawnEntity("CFDetectMagic", _map.Local(0, 0)), user, user));
        });
    }
}
