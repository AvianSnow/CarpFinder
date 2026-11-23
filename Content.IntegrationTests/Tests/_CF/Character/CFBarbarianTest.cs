using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Overlays;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Barbarian's instincts and the feats that change what a Rage does.</summary>
[TestFixture]
[TestOf(typeof(CFActivitySystem))]
public sealed class CFBarbarianTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFFeatPrototype> AcuteVision = "CFAcuteVision";
    private static readonly ProtoId<CFFeatPrototype> AdrenalineRush = "CFAdrenalineRush";
    private static readonly ProtoId<CFFeatPrototype> SupernaturalSenses = "CFSupernaturalSenses";
    private static readonly ProtoId<CFClassChoicePrototype> Fury = "InstinctFury";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFBarbarianTestHero
  components:
  - type: CFCombatant
    level: 3
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFBarbarianTestHero
  id: CFBarbarianTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFBarbarianTestFoe
  id: CFBarbarianTestFireproofFoe
  components:
  - type: CFResistances
    entries:
    - damageType: Fire
      amount: 999
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFActivitySystem Activities => SEntMan.System<CFActivitySystem>();

    private EntityUid Spawn(string id = "CFBarbarianTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private CFCombatOptionsComponent Barbarian(string instinct, string pick, out EntityUid hero)
    {
        hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Class = "Barbarian" };
        build.ClassChoices.Add(instinct);
        if (pick != null)
            build.OptionPicks.Add(new CFOptionPick { Source = instinct, Slot = 0, Value = pick });

        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCFBuild(build));
        return SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
    }

    /// <summary>The most one unarmed Strike took off a fresh foe, over several attempts.</summary>
    private int HardestHit(EntityUid hero, string foeId)
    {
        var strikes = SEntMan.System<CFStrikeSystem>();
        var most = 0;
        for (var i = 0; i < 8; i++)
        {
            var foe = Spawn(foeId, 1);
            strikes.PerformStrike(hero, foe, strikes.UnarmedStats(hero), hero, melee: true);
            most = System.Math.Max(most, StartHp - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp);
            SEntMan.DeleteEntity(foe);
        }

        return most;
    }

    [Test]
    public async Task EachInstinctChangesTheRage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string ape = null, dragon = null, spirit = null;
        int dragonDamage = 0, caster = 0, oversized = 0;
        bool heals = false;
        await Server.WaitPost(() =>
        {
            ape = Barbarian("InstinctAnimal", "pick:AnimalApe", out _).RageAttackType;
            var diabolic = Barbarian("InstinctDragon", "pick:DragonDiabolic", out _);
            dragon = diabolic.RageDamageType;
            dragonDamage = diabolic.RageDamage;
            spirit = Barbarian("InstinctSpirit", null, out _).RageDamageType;
            var superstition = Barbarian("InstinctSuperstition", null, out _);
            caster = superstition.RageCasterDamage;
            heals = superstition.RageHeals;
            oversized = Barbarian("InstinctGiant", null, out _).RageOversizedDamage;
        });

        Assert.Multiple(() =>
        {
            Assert.That(ape, Is.EqualTo("Bludgeoning"));
            Assert.That(dragon, Is.EqualTo("Fire"));
            Assert.That(dragonDamage, Is.EqualTo(4));
            Assert.That(spirit, Is.EqualTo("Spirit"));
            Assert.That(caster, Is.EqualTo(4));
            Assert.That(heals, Is.True);
            Assert.That(oversized, Is.EqualTo(6));
        });
    }

    [Test]
    public async Task ADragonsRageDamageIsItsOwnType()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        int plain = 0, typed = 0, resisted = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.Raging = true;
            options.RageDamage = 100;
            plain = HardestHit(hero, "CFBarbarianTestFireproofFoe");

            options.RageDamageType = "Fire";
            typed = HardestHit(hero, "CFBarbarianTestFoe");
            resisted = HardestHit(hero, "CFBarbarianTestFireproofFoe");
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.GreaterThanOrEqualTo(100), "Untyped, it is part of the fist's damage");
            Assert.That(typed, Is.GreaterThanOrEqualTo(100), "As fire it still lands on a creature that burns");
            Assert.That(resisted, Is.LessThan(100), "And a creature that resists fire shrugs it off");
        });
    }

    [Test]
    public async Task SuperstitionHitsCastersHarderAndHealsOnRage()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        bool before = true, after = false;
        int healed = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var foe = Spawn("CFBarbarianTestFoe", 1);
            before = conditions.CastRecently(foe);
            conditions.NoteCast(foe);
            after = conditions.CastRecently(foe);

            var hero = Spawn();
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.CanRage = true;
            options.RageHeals = true;
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            combat.CurrentHp = 1000;
            Activities.SetRaging(hero, true);
            healed = combat.CurrentHp - 1000;
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
            Assert.That(healed, Is.EqualTo(3), "Level 3 and Constitution +0");
        });
    }

    [Test]
    public async Task AGiantStartsWithAnOversizedWeaponThatMakesItClumsy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string name = null;
        int empty = -1, holding = -1;
        await Server.WaitPost(() =>
        {
            Barbarian("InstinctGiant", "pick:GiantGreatsword", out var hero);
            var conditions = SEntMan.System<CFConditionSystem>();
            empty = conditions.Clumsy(hero);
            var query = SEntMan.AllEntityQueryEnumerator<CFOversizedWeaponComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                name = meta.EntityName;
                var transform = SEntMan.System<SharedTransformSystem>();
                transform.SetCoordinates(uid, SEntMan.GetComponent<TransformComponent>(hero).Coordinates);
                SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, uid, checkActionBlocker: false);
            }

            holding = conditions.Clumsy(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(name, Does.StartWith("oversized"));
            Assert.That(empty, Is.EqualTo(0));
            Assert.That(holding, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RagingBringsDarkvisionAndMoreBulk()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool calm = true, raging = false, ended = true;
        int strengthCalm = 0, strengthRaging = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var encumbrance = SEntMan.System<CFEncumbranceSystem>();
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).CanRage = true;
            Features.ApplyFeat(hero, Server.ProtoMan.Index(AcuteVision));
            Features.ApplyFeat(hero, Server.ProtoMan.Index(AdrenalineRush));
            calm = SEntMan.HasComponent<NightVisionComponent>(hero);
            strengthCalm = encumbrance.Strength(hero);

            Activities.SetRaging(hero, true);
            raging = SEntMan.HasComponent<NightVisionComponent>(hero);
            strengthRaging = encumbrance.Strength(hero);

            Activities.SetRaging(hero, false);
            ended = SEntMan.HasComponent<NightVisionComponent>(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.False);
            Assert.That(raging, Is.True);
            Assert.That(ended, Is.False);
            Assert.That(strengthRaging - strengthCalm, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SupernaturalSensesEaseTheFlatCheckWhileRaging()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int calm = 0, raging = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFBarbarianTestFoe", 1);
            var conditions = SEntMan.System<CFConditionSystem>();
            Features.ApplyFeat(hero, Server.ProtoMan.Index(SupernaturalSenses));

            // A flat check no die can pass, so only the eased one ever does.
            var cond = SEntMan.EnsureComponent<CFConditionComponent>(foe);
            cond.Concealed = true;
            cond.FlatCheckDc = 21;
            for (var i = 0; i < 40; i++)
            {
                if (conditions.PassFlatCheck(hero, foe))
                    calm++;
            }

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).Raging = true;
            for (var i = 0; i < 40; i++)
            {
                if (conditions.PassFlatCheck(hero, foe))
                    raging++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.EqualTo(0));
            Assert.That(raging, Is.GreaterThan(20), "DC 3 passes nine times in ten");
        });
    }

    [Test]
    public async Task FuryOffersABonusFeatAndShakeItOffNeedsARage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        List<string> offered = null;
        bool calm = true, raging = false;
        var frightened = 0;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Class = "Barbarian" };
            build.ClassChoices.Add("InstinctFury");
            offered = CFOptionChoices.Allowed(Server.ProtoMan, Server.ProtoMan.Index(Fury).Choices[0], build, "Human");

            var hero = Spawn();
            Features.GrantAction(hero, "CFActShakeItOff");
            var actions = SEntMan.System<SharedActionsSystem>();
            var action = actions.GetActions(hero)
                .First(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == "CFActShakeItOff");
            var conditions = SEntMan.System<CFConditionSystem>();
            conditions.ApplyFrightened(hero, 2);

            var attempt = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            calm = !attempt.Cancelled;

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).Raging = true;
            attempt = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            raging = !attempt.Cancelled;
            if (raging)
                actions.PerformAction(hero, action);

            frightened = conditions.Frightened(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("CFAcuteVision"));
            Assert.That(offered, Does.Not.Contain("CFScarsofSteel"), "A feat of another level");
            Assert.That(calm, Is.False, "The rage trait");
            Assert.That(raging, Is.True);
            Assert.That(frightened, Is.EqualTo(1));
        });
    }
}
