using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Summoner's evolution feats, link spells and eidolon types, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFEidolonSystem))]
public sealed class CFSummonerTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSummonerHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 40
    currentHp: 40
    classId: Summoner
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
  - type: CFExperience

- type: entity
  parent: CFSummonerHero
  id: CFSummonerFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    armorClass: 1
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFSummonerFoe
  id: CFSummonerUnholy
  components:
  - type: CFTrait
    traits:
    - Unholy
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFEidolonSystem Eidolons => SEntMan.System<CFEidolonSystem>();

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private EntityUid Spawn(string id = "CFSummonerHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Summoner(params string[] choices)
    {
        var hero = Spawn();
        foreach (var feature in Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Summoner")).Features.Where(f => f.Level == 1))
        {
            Features.Apply(hero, feature.Grants, feature.Name);
        }

        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }

        return hero;
    }

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private void Pick(EntityUid hero, string source, string value)
    {
        SEntMan.GetComponent<CFExperienceComponent>(hero).OptionPicks.Add(new CFOptionPick { Source = source, Slot = 0, Value = value });
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private CFEidolonLinkComponent Link(EntityUid hero) => SEntMan.GetComponent<CFEidolonLinkComponent>(hero);

    private EntityUid Manifest(EntityUid hero)
    {
        Actions.PerformAction(hero, Action(hero, "CFActManifestEidolon"), new CFActivityInstantEvent());
        return Link(hero).Eidolon;
    }

    private int Hp(EntityUid mob) => SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    /// <summary>Energy Heart sets the attack's type and a resistance. Advanced Weaponry makes it versatile.</summary>
    [Test]
    public async Task EnergyHeartAndAdvancedWeaponryShapeTheAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var type = "";
        var versatile = "";
        var resisted = 10;
        await Server.WaitPost(() =>
        {
            var hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Pick(hero, "CFEnergyHeart", "Fire");
            Pick(hero, "CFAdvancedWeaponry", "pick:VersatileP");
            GiveFeat(hero, "CFEnergyHeart");
            GiveFeat(hero, "CFAdvancedWeaponry");
            var eidolon = Manifest(hero);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(eidolon);
            type = options.NaturalType;
            versatile = options.NaturalVersatile;
            resisted = SEntMan.System<CFBonusSystem>().ModifyDamage(eidolon, "Fire", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(type, Is.EqualTo("Fire"));
            Assert.That(versatile, Is.EqualTo("Piercing"));
            Assert.That(resisted, Is.LessThan(10));
        });
    }

    /// <summary>Ranged Combatant reaches a creature 20 feet away, where the eidolon's fists cannot.</summary>
    [Test]
    public async Task RangedCombatantShootsFromAfar()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            Pick(hero, "CFRangedCombatant", "Cold");
            GiveFeat(hero, "CFRangedCombatant");
            Manifest(hero);
            var foe = Spawn("CFSummonerFoe", 5);
            Actions.PerformAction(hero, Action(hero, "CFActEidolonShot"), new CFActivityTargetEvent { Target = foe });
            hurt = 500 - Hp(foe);
        });

        Assert.That(hurt, Is.GreaterThan(0));
    }

    /// <summary>Hallowed Strikes add 1 spirit against the unholy, and Spirit Touch adds 1 void to the living.</summary>
    [Test]
    public async Task SpiritStrikesAddToAHit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int hallowed = 0, plain = 0, touched = 0;
        await Server.WaitPost(() =>
        {
            var angel = Summoner("EidolonAngel", "EidolonKeyStrength", "EidolonAttackSlashing");
            var eidolon = Manifest(angel);
            var stats = SEntMan.System<CFStrikeSystem>().UnarmedStats(eidolon);
            var unholy = Spawn("CFSummonerUnholy", 2);
            var foe = Spawn("CFSummonerFoe", 3);
            Eidolons.NoteHit(eidolon, unholy, stats, false, 5);
            Eidolons.NoteHit(eidolon, foe, stats, false, 5);
            hallowed = 500 - Hp(unholy);
            plain = 500 - Hp(foe);

            var psychopomp = Summoner("EidolonPsychopomp", "EidolonKeyStrength", "EidolonAttackSlashing");
            SEntMan.System<SharedTransformSystem>().SetCoordinates(psychopomp, _map.Local(5, 0));
            var ghost = Manifest(psychopomp);
            var living = Spawn("CFSummonerFoe", 6);
            Eidolons.NoteHit(ghost, living, stats, false, 5);
            touched = 500 - Hp(living);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hallowed, Is.EqualTo(1));
            Assert.That(plain, Is.EqualTo(0));
            Assert.That(touched, Is.EqualTo(1));
        });
    }

    /// <summary>Reactive Dismissal takes twice the level off the blow and sends the eidolon away for 6 seconds.</summary>
    [Test]
    public async Task ReactiveDismissalSoftensTheBlowAndUnmanifests()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var lost = 0;
        var refused = true;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            GiveFeat(hero, "CFReactiveDismissal");
            var eidolon = Manifest(hero);
            var foe = Spawn("CFSummonerFoe", 2);
            Effects.DealDamage(foe, eidolon, 5, "Fire", false);
            lost = 40 - Hp(eidolon);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitPost(() =>
        {
            refused = !Link(hero).Eidolon.IsValid();
            Actions.PerformAction(hero, Action(hero, "CFActManifestEidolon"), new CFActivityInstantEvent());
        });

        Assert.Multiple(() =>
        {
            Assert.That(lost, Is.EqualTo(3), "Twice the level, 2, comes off 5.");
            Assert.That(refused, Is.True, "It unmanifested.");
            Assert.That(Link(hero).Eidolon.IsValid(), Is.False, "And cannot come back for 6 seconds.");
        });
    }

    /// <summary>Unfetter Eidolon lets the eidolon wander past the tether for a minute.</summary>
    [Test]
    public async Task UnfetterEidolonLoosensTheTether()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            GiveFeat(hero, "CFUnfetterEidolon");
            var eidolon = Manifest(hero);
            Effects.ResolveTarget(Action(hero, "CFUnfetterEidolon"), hero, hero);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(eidolon, _map.Local(7, 0).Offset(new System.Numerics.Vector2(30f, 0f)));
        });
        await Server.WaitRunTicks(40);

        Assert.That(Link(hero).Eidolon.IsValid(), Is.True, "Still manifested, 30 tiles away.");
    }

    /// <summary>Lifelink Surge heals the shared pool every 6 seconds, and Extend Boost boosts for 18 seconds.</summary>
    [Test]
    public async Task LinkSpellsHealAndBoost()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var boost = 0;
        var pulse = 0;
        await Server.WaitPost(() =>
        {
            hero = Summoner("EidolonBeast", "EidolonKeyStrength", "EidolonAttackSlashing");
            GiveFeat(hero, "CFLifelinkSurge");
            GiveFeat(hero, "CFExtendBoost");
            var eidolon = Manifest(hero);
            SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp = 20;
            Effects.ResolveTarget(Action(hero, "CFExtendBoost"), hero, hero);
            boost = SEntMan.System<CFBonusSystem>().Total(eidolon, CFBonusStat.Damage);
            Effects.ResolveTarget(Action(hero, "CFLifelinkSurge"), hero, hero);
            var healing = SEntMan.GetComponent<CFFastHealingComponent>(eidolon);
            pulse = healing.PerPulse;
            healing.Next = Timing.CurTime;
        });
        await Server.WaitRunTicks(10);

        Assert.Multiple(() =>
        {
            Assert.That(boost, Is.EqualTo(2));
            Assert.That(pulse, Is.EqualTo(4));
            Assert.That(Hp(hero), Is.GreaterThanOrEqualTo(24), "One pulse of 4 reached the shared pool.");
        });
    }

    /// <summary>Dutiful Retaliation: the Devotion Phantom hits back when its Summoner is hurt. Surprising Anatomy: the Aberrant hits back when it is.</summary>
    [Test]
    public async Task EidolonsRetaliate()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int devotion = 0, aberrant = 0;
        await Server.WaitPost(() =>
        {
            // A natural 1 misses even AC 1, so each blow is struck a few times with the reaction given back.
            var hero = Summoner("EidolonDevotionPhantom", "EidolonKeyStrength", "EidolonAttackSlashing");
            var phantom = Manifest(hero);
            var foe = Spawn("CFSummonerFoe", 2);
            for (var i = 0; i < 5 && devotion == 0; i++)
            {
                SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ReactionSpent = 0;
                Effects.DealDamage(foe, hero, 1, "Fire", false);
                devotion = 500 - Hp(foe);
            }

            var other = Summoner("EidolonAberrant", "EidolonKeyStrength", "EidolonAttackSlashing");
            SEntMan.System<SharedTransformSystem>().SetCoordinates(other, _map.Local(4, 0));
            var eidolon = Manifest(other);
            var attacker = Spawn("CFSummonerFoe", 5);
            for (var i = 0; i < 5 && aberrant == 0; i++)
            {
                SEntMan.GetComponent<CFCombatOptionsComponent>(eidolon).ReactionSpent = 0;
                Effects.DealDamage(attacker, eidolon, 1, "Fire", false);
                aberrant = 500 - Hp(attacker);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(devotion, Is.GreaterThan(0));
            Assert.That(aberrant, Is.GreaterThan(0));
        });
    }

    /// <summary>Every Summoner feat applies, and every eidolon type can still be taken.</summary>
    [Test]
    public async Task EverySummonerFeatCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        var types = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class == "Summoner"))
            {
                Features.Apply(Spawn(), feat.Grants, feat.ID);
                count++;
            }

            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Summoner"))
            {
                Features.Apply(Spawn(), choice.Grants, choice.ID);
                types++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(23));
            Assert.That(types, Is.EqualTo(20));
        });
    }
}
