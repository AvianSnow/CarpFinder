using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Alchemist: versatile vials, Quick Alchemy, bomb training and the Bomber's splash.</summary>
[TestFixture]
[TestOf(typeof(CFQuickAlchemySystem))]
public sealed class CFAlchemistTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly EntProtoId Elixir = "CFElixirOfLifeMinor";
    private static readonly EntProtoId AcidVial = "CFVersatileVialAcid";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAlchemistTestHero
  components:
  - type: CFCombatant
    level: 4
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Dexterity: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFAlchemistTestHero
  id: CFAlchemistTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFQuickAlchemySystem Alchemy => SEntMan.System<CFQuickAlchemySystem>();

    private EntityUid Spawn(string id = "CFAlchemistTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Build(string cls, string choice)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Class = cls };
        build.ClassChoices.Add(choice);
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCFBuild(build));
        return hero;
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    [Test]
    public async Task AnAlchemistHasItsVialsAndIsTrainedWithBombs()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int vials = 0, most = 0, afterUse = 0, afterRest = 0;
        bool quick = false, quickVial = false, bomber = false;
        CFProficiencyRank alchemist = default, druid = default;
        await Server.WaitPost(() =>
        {
            var hero = Build("Alchemist", "FieldBomber");
            quick = HasAction(hero, "CFActQuickAlchemy");
            quickVial = HasAction(hero, "CFActQuickVial");
            bomber = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).BomberField;
            most = Alchemy.MaxVials(hero);
            vials = Alchemy.Vials(hero);

            SEntMan.System<SharedChargesSystem>().TryUseCharge(Alchemy.VialAction(hero)!.Value);
            afterUse = Alchemy.Vials(hero);
            SEntMan.System<CFDailyUseSystem>().Restore(hero);
            afterRest = Alchemy.Vials(hero);

            var weapons = SEntMan.System<CFWeaponSystem>();
            var fire = Spawn("CFAlchemistsFireLesser");
            weapons.TryStats(fire, out var stats, thrown: true);
            alchemist = weapons.Rank(SEntMan.GetComponent<CFCombatantComponent>(hero), stats);
            druid = weapons.Rank(SEntMan.GetComponent<CFCombatantComponent>(Build("Druid", "OrderStorm")), stats);
        });

        Assert.Multiple(() =>
        {
            Assert.That(quick, Is.True);
            Assert.That(quickVial, Is.True);
            Assert.That(bomber, Is.True);
            Assert.That(most, Is.GreaterThanOrEqualTo(2));
            Assert.That(vials, Is.EqualTo(most), "The pool starts full");
            Assert.That(afterUse, Is.EqualTo(most - 1));
            Assert.That(afterRest, Is.EqualTo(most), "A long rest makes them all again");
            Assert.That(alchemist, Is.GreaterThanOrEqualTo(CFProficiencyRank.Trained), "Alchemical bombs are their own training");
            Assert.That(druid, Is.LessThan(CFProficiencyRank.Trained), "A bomb is a martial weapon to anyone else");
        });
    }

    [Test]
    public async Task QuickAlchemyMakesAShortLivedItemAndAVialGrowsWithLevel()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool held = false;
        int dice = 0, splash = 0;
        float bombLife = 0, elixirLife = 0, enduringLife = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var vial = Alchemy.Make(hero, AcidVial, 6f, vial: true)!.Value;
            held = SEntMan.System<SharedHandsSystem>().IsHolding(hero, vial);
            dice = SEntMan.GetComponent<CFWeaponComponent>(vial).DamageDice;
            splash = SEntMan.GetComponent<CFBombComponent>(vial).Splash;
            bombLife = SEntMan.GetComponent<TimedDespawnComponent>(vial).Lifetime;

            elixirLife = SEntMan.GetComponent<TimedDespawnComponent>(Alchemy.Make(hero, Elixir, 6f, vial: false)!.Value).Lifetime;
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).EnduringAlchemy = true;
            enduringLife = SEntMan.GetComponent<TimedDespawnComponent>(Alchemy.Make(Spawn(x: 1), Elixir, 6f, vial: false)!.Value).Lifetime;
            enduringLife = SEntMan.GetComponent<TimedDespawnComponent>(Alchemy.Make(hero, Elixir, 6f, vial: false)!.Value).Lifetime;
        });

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.True);
            Assert.That(dice, Is.EqualTo(2), "Level 4: 2d6");
            Assert.That(splash, Is.EqualTo(2));
            Assert.That(bombLife, Is.EqualTo(6f));
            Assert.That(elixirLife, Is.EqualTo(6f));
            Assert.That(enduringLife, Is.EqualTo(12f), "Enduring Alchemy");
        });
    }

    [Test]
    public async Task ABombersSplashSparesItsAllies()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        int careless = 0, careful = 0;
        await Server.WaitPost(() =>
        {
            var strikes = SEntMan.System<CFStrikeSystem>();
            var weapons = SEntMan.System<CFWeaponSystem>();
            int AllyHurt(bool bomber)
            {
                var hero = Spawn();
                SEntMan.GetComponent<CFCombatOptionsComponent>(hero).BomberField = bomber;
                var ally = Spawn(x: 1);
                var most = 0;

                // A critical failure splashes nobody, so several throws.
                for (var i = 0; i < 6; i++)
                {
                    var foe = Spawn("CFAlchemistTestFoe", 1);
                    var bomb = Spawn("CFVersatileVialAcid");
                    weapons.TryStats(bomb, out var stats, thrown: true);
                    strikes.PerformStrike(hero, foe, stats, bomb, melee: false, countMap: false, fixedMap: 0);
                    most = System.Math.Max(most, StartHp - SEntMan.GetComponent<CFCombatantComponent>(ally).CurrentHp);
                    SEntMan.DeleteEntity(foe);
                }

                SEntMan.DeleteEntity(ally);
                SEntMan.DeleteEntity(hero);
                return most;
            }

            careless = AllyHurt(false);
            careful = AllyHurt(true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(careless, Is.GreaterThan(0), "A bomb splashes whoever stands beside the target");
            Assert.That(careful, Is.EqualTo(0), "A Bomber's does not splash allies");
        });
    }
}
