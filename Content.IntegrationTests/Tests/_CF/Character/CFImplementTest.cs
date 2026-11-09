using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Thaumaturge implements, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFImplementSystem))]
public sealed class CFImplementTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFImplementHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFImplementHero
  id: CFImplementFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFImplementSystem Implements => SEntMan.System<CFImplementSystem>();

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Spawn(string id = "CFImplementHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void Choose(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
    }

    [Test]
    public async Task EmpowermentNeedsAnImplementAndFreeHands()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool nothing = true, implement = false, withPistol = false, pistolAlone = true, withMusket = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            nothing = Implements.Empowers(hero);
            var amulet = Spawn("CFImplementAmulet");
            Hands.TryPickupAnyHand(hero, amulet);
            implement = Implements.Empowers(hero);
            var pistol = Spawn("CFFlintlockPistol");
            Hands.TryPickupAnyHand(hero, pistol);
            withPistol = Implements.Empowers(hero);
            Hands.TryDrop(hero, amulet);
            pistolAlone = Implements.Empowers(hero);
            Hands.TryDrop(hero, pistol);

            Hands.TryPickupAnyHand(hero, Spawn("CFImplementAmulet"));
            Hands.TryPickupAnyHand(hero, Spawn("CFFlintlockMusket"));
            withMusket = Implements.Empowers(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(nothing, Is.False);
            Assert.That(implement, Is.True);
            Assert.That(withPistol, Is.True);
            Assert.That(pistolAlone, Is.False);
            Assert.That(withMusket, Is.False, "A two-handed weapon leaves no room.");
        });
    }

    [Test]
    public async Task AWeaponImplementIsAnyOneHandedWeapon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = true, after = false, empowered = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Hands.TryPickupAnyHand(hero, Spawn("CFFlintlockPistol"));
            before = Implements.IsHolding(hero, CFImplementKind.Weapon);
            Choose(hero, "ImplementWeapon");
            after = Implements.IsHolding(hero, CFImplementKind.Weapon);
            empowered = Implements.Empowers(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
            Assert.That(empowered, Is.True);
        });
    }

    [Test]
    public async Task EveryImplementIsTheKindItsNameSays()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            foreach (var kind in new[] { "Amulet", "Bell", "Censer", "Chalice", "Lantern", "Mirror", "Regalia", "Shield", "Tome", "Wand" })
            {
                var item = Spawn("CFImplement" + kind);
                Assert.That(SEntMan.GetComponent<CFImplementComponent>(item).Kind.ToString(), Is.EqualTo(kind));
            }
        });
    }

    [Test]
    public async Task AmuletsAbeyanceNeedsTheAmuletAndTheExploit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int noExploit = 0, noAmulet = 0, both = 0;
        await Server.WaitPost(() =>
        {
            var reactions = SEntMan.System<CFReactionSystem>();
            var hero = Spawn();
            Choose(hero, "ImplementAmulet");
            var ally = Spawn(x: 1);
            var foe = Spawn("CFImplementFoe", 1);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);

            noAmulet = (options.Exploited = foe) == foe ? reactions.AnswerDamage(ally, foe, 20, "Slashing") : 0;
            Hands.TryPickupAnyHand(hero, Spawn("CFImplementAmulet"));
            options.Exploited = null;
            noExploit = reactions.AnswerDamage(ally, foe, 20, "Slashing");
            options.Exploited = foe;
            both = reactions.AnswerDamage(ally, foe, 20, "Slashing");
        });

        Assert.Multiple(() =>
        {
            Assert.That(noAmulet, Is.EqualTo(20));
            Assert.That(noExploit, Is.EqualTo(20));
            Assert.That(both, Is.EqualTo(17), "2 + level 1 comes off.");
        });
    }

    [Test]
    public async Task RegaliaHelpsAlliesWhileHeld()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, ally = default;
        var fear = new HashSet<ProtoId<CFTraitPrototype>> { "Fear" };
        int without = 1, with = 0, own = 0;
        await Server.WaitPost(() =>
        {
            hero = Spawn();
            Features.Apply(hero, Server.ProtoMan.Index<CFClassPrototype>("Champion").Features.First(f => f.Name == "Champion's Aura").Grants, "aura");
            Choose(hero, "ImplementRegalia");
            ally = Spawn(x: 1);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            without = SEntMan.System<CFBonusSystem>().Total(ally, CFBonusStat.Saves, traits: fear);
            Hands.TryPickupAnyHand(hero, Spawn("CFImplementRegalia"));
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            with = SEntMan.System<CFBonusSystem>().Total(ally, CFBonusStat.Saves, traits: fear);
            own = SEntMan.System<CFBonusSystem>().Total(hero, CFBonusStat.Saves, traits: fear);
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.EqualTo(0));
            Assert.That(with, Is.EqualTo(1));
            Assert.That(own, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AWandActionNeedsTheWand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool empty = true, held = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var action = SEntMan.SpawnEntity("CFActFlingMagicFire", Robust.Shared.Map.MapCoordinates.Nullspace);
            var first = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref first);
            empty = !first.Cancelled;
            Hands.TryPickupAnyHand(hero, Spawn("CFImplementWand"));
            var second = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref second);
            held = !second.Cancelled;
        });

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.False);
            Assert.That(held, Is.True);
        });
    }

    [Test]
    public async Task EveryOptionCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Thaumaturge"))
            {
                var hero = Spawn();
                Features.Apply(hero, choice.Grants, choice.ID);
                foreach (var feature in choice.Features)
                {
                    Features.Apply(hero, feature.Grants, choice.ID);
                }

                count++;
            }
        });

        Assert.That(count, Is.EqualTo(26));
    }
}
