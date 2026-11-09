using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Server.CF.Companions;
using Content.Server.Ghost.Roles.Components;
using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>A Summoner's eidolon: manifesting it, what it is, and the Hit Points the two share.</summary>
[TestFixture]
[TestOf(typeof(CFEidolonServerSystem))]
public sealed class CFEidolonTest : GameTest
{
    private static readonly Box2i Footprint = new(-2, -2, 4, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFEidolonHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 40
    currentHp: 40
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
  - type: CFExperience

- type: entity
  parent: CFEidolonHero
  id: CFEidolonFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    armorClass: 1
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFEidolonServerSystem Eidolons => SEntMan.System<CFEidolonServerSystem>();

    private EntityUid Spawn(string id, int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Summoner(params string[] choices)
    {
        var hero = Spawn("CFEidolonHero");
        foreach (var feature in Server.ProtoMan.Index<CFClassPrototype>("Summoner").Features.Where(f => f.Level == 1))
        {
            Features.Apply(hero, feature.Grants, feature.Name);
        }

        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }

        return hero;
    }

    private EntityUid Action(EntityUid hero, string id)
    {
        foreach (var action in SEntMan.System<SharedActionsSystem>().GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        Assert.Fail($"{id} was not granted");
        return default;
    }

    private CFEidolonLinkComponent Link(EntityUid hero) => SEntMan.GetComponent<CFEidolonLinkComponent>(hero);

    private void Manifest(EntityUid hero)
    {
        var ev = new CFActivityInstantEvent { Performer = hero };
        SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFActManifestEidolon"), ev);
    }

    private CFCombatantComponent Combat(EntityUid mob) => SEntMan.GetComponent<CFCombatantComponent>(mob);

    [Test]
    public async Task ManifestBringsItAndSendsItBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, eidolon = default;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Manifest(hero);
            eidolon = Link(hero).Eidolon;
        });
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(eidolon), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(eidolon).EntityName, Is.EqualTo("beast eidolon"));
            Manifest(hero);
            Assert.That(Link(hero).Eidolon.IsValid(), Is.False);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(eidolon), Is.True));
    }

    [TestCase("EidolonKeyStrength", 17, false)]
    [TestCase("EidolonKeyDexterity", 18, true)]
    public async Task ItsStatisticsFollowTheBuild(string key, int armor, bool finesse)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Summoner("EidolonBeast", key, "EidolonAttackPiercing");
            Manifest(hero);
            var eidolon = Link(hero).Eidolon;
            Assert.That(Combat(eidolon).ArmorClass, Is.EqualTo(armor));

            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(eidolon);
            Assert.That(options.NaturalType, Is.EqualTo("piercing"));
            Assert.That(options.NaturalFinesse, Is.EqualTo(finesse));
            Assert.That(Combat(eidolon).MaxHp, Is.EqualTo(40));
        });
    }

    [Test]
    public async Task AGhostCanTakeIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Summoner("EidolonPlant", "EidolonKeyStrength", "EidolonAttackBludgeoning");
            Manifest(hero);
            var eidolon = Link(hero).Eidolon;
            Assert.That(SEntMan.HasComponent<GhostRoleComponent>(eidolon), Is.True);
            Assert.That(SEntMan.HasComponent<GhostTakeoverAvailableComponent>(eidolon), Is.True);
            Assert.That(SEntMan.HasComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(eidolon), Is.True, "a player in the body has to be able to swing");
            Assert.That(SEntMan.HasComponent<Content.Shared.Movement.Components.InputMoverComponent>(eidolon), Is.True);
        });
    }

    [Test]
    public async Task DamageToEitherBodyComesOutOfOnePool()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, eidolon = default;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Manifest(hero);
            eidolon = Link(hero).Eidolon;
            Combat(eidolon).CurrentHp -= 5;
        });
        await Pair.RunTicksSync(2);
        await Server.WaitPost(() =>
        {
            Assert.That(Combat(hero).CurrentHp, Is.EqualTo(35));
            Assert.That(Combat(eidolon).CurrentHp, Is.EqualTo(35));
            Combat(hero).CurrentHp -= 10;
            Combat(eidolon).CurrentHp -= 1;
        });
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Combat(hero).CurrentHp, Is.EqualTo(24));
            Assert.That(Combat(eidolon).CurrentHp, Is.EqualTo(24));
        });
    }

    [Test]
    public async Task HealingEitherBodyHealsBoth()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, eidolon = default;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Combat(hero).CurrentHp = 20;
            Manifest(hero);
            eidolon = Link(hero).Eidolon;
            Combat(eidolon).CurrentHp += 8;
        });
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Combat(hero).CurrentHp, Is.EqualTo(28));
        });
    }

    [Test]
    public async Task ABlowThatEmptiesThePoolKnocksTheSummonerOutAndEndsTheEidolon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, eidolon = default;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Manifest(hero);
            eidolon = Link(hero).Eidolon;
            Combat(eidolon).CurrentHp = 0;
        });
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Combat(hero).CurrentHp, Is.EqualTo(0));
            Assert.That(Link(hero).Eidolon.IsValid(), Is.False);
            Assert.That(SEntMan.Deleted(eidolon), Is.True);
        });
    }

    [Test]
    public async Task ItCannotStrayPastTheTether()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Manifest(hero);
            var link = Link(hero);
            var pos = SEntMan.System<SharedTransformSystem>().GetWorldPosition(hero);
            var xform = SEntMan.GetComponent<TransformComponent>(link.Eidolon);
            SEntMan.System<SharedTransformSystem>().SetWorldPosition(link.Eidolon, pos + new System.Numerics.Vector2(40, 0));
            Assert.That(xform.MapID, Is.Not.EqualTo(MapId.Nullspace));
        });
        await Pair.RunSeconds(2);
        await Server.WaitAssertion(() => Assert.That(Link(hero).Eidolon.IsValid(), Is.False));
    }

    [Test]
    public async Task BoostEidolonLandsOnTheEidolon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Manifest(hero);
            var effects = SEntMan.System<CFEffectSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();
            effects.ResolveTarget(Action(hero, "CFBoostEidolon"), hero, hero);
            Assert.That(bonuses.Total(Link(hero).Eidolon, CFBonusStat.Damage), Is.EqualTo(2));
            Assert.That(bonuses.Total(hero, CFBonusStat.Damage), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AnEidolonStrikeNeedsItInReach()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Summoner("EidolonAngerPhantom", "EidolonKeyStrength", "EidolonAttackSlashing");
            var foe = SEntMan.SpawnEntity("CFEidolonFoe", _map.Local(4, 2));
            var far = new CFActivityTargetEvent { Performer = hero, Target = foe };
            SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFActFuriousStrike"), far);
            Assert.That(far.Handled, Is.False, "not manifested");

            Manifest(hero);
            var away = new CFActivityTargetEvent { Performer = hero, Target = foe };
            SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFActFuriousStrike"), away);
            Assert.That(away.Handled, Is.False, "out of reach");

            var near = Spawn("CFEidolonFoe", 1);
            var hit = new CFActivityTargetEvent { Performer = hero, Target = near };
            SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFActFuriousStrike"), hit);
            Assert.That(hit.Handled, Is.True);
        });
    }

    [Test]
    public async Task DragonBreathComesFromTheEidolonAndLeavesItStanding()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, eidolon = default;
        var foes = new EntityUid[6];
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonDragon", "EidolonKeyStrength", "EidolonAttackSlashing");
            Manifest(hero);
            eidolon = Link(hero).Eidolon;
            for (var i = 0; i < foes.Length; i++)
            {
                foes[i] = Spawn("CFEidolonFoe", 2);
            }

            SEntMan.System<CFEffectSystem>().ResolveTarget(Action(hero, "CFActDragonBreath"), hero, hero);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            // Any one of them can save, but not all six.
            Assert.That(foes.Sum(foe => 500 - Combat(foe).CurrentHp), Is.GreaterThan(0));
            Assert.That(SEntMan.Deleted(eidolon), Is.False);
            Assert.That(Link(hero).Eidolon, Is.EqualTo(eidolon));
            Assert.That(Combat(hero).CurrentHp, Is.EqualTo(40));
        });
    }

    [Test]
    public async Task AlacritousActionSpeedsTheEidolon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Features.Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>("CFAlacritousAction").Grants, "feat");
            Manifest(hero);
            Assert.That(Combat(Link(hero).Eidolon).SpeedFeet, Is.EqualTo(35));
        });
    }

    [Test]
    public async Task EveryEidolonOptionCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Summoner"))
            {
                Features.Apply(Spawn("CFEidolonHero"), choice.Grants, choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(20));
    }
}
