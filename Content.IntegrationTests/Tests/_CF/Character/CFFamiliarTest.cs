using Content.Shared.CF.Preferences;
using Content.Server.CF.Character;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Companions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Familiars: each has a body of its own, and that body follows and does not fight.</summary>
[TestFixture]
[TestOf(typeof(CFCompanionSystem))]
public sealed class CFFamiliarTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFamiliarTestMaster
  components:
  - type: CFCombatant
    level: 1
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFFamiliarTestMaster
  id: CFFamiliarTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    [Test]
    public async Task EveryFamiliarHasAPeacefulBodyOfItsOwn()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var missing = new List<string>();
        var fighters = new List<string>();
        var seen = new HashSet<string>();
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var pet in Server.ProtoMan.EnumeratePrototypes<CFPetPrototype>())
            {
                if (pet.Kind != CFPetKind.Familiar)
                    continue;

                count++;
                if (!Server.ProtoMan.HasIndex<EntityPrototype>(pet.Entity) || !seen.Add(pet.Entity))
                {
                    missing.Add(pet.ID);
                    continue;
                }

                var body = SEntMan.SpawnEntity(pet.Entity, map.Local(0, 0));
                if (!SEntMan.TryGetComponent(body, out CFCompanionComponent companion) || !companion.Peaceful)
                    fighters.Add(pet.ID);

                SEntMan.DeleteEntity(body);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(missing, Is.Empty, "Each familiar names a body, and no two share one.");
            Assert.That(fighters, Is.Empty, "A familiar does not fight.");
        });
    }

    [Test]
    public async Task AFamiliarKeepsToItsMasterAndLeavesEnemiesAlone()
    {
        var map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid master = default, cat = default, foe = default;
        await Server.WaitPost(() =>
        {
            master = SEntMan.SpawnEntity("CFFamiliarTestMaster", map.Local(0, 0));
            foe = SEntMan.SpawnEntity("CFFamiliarTestFoe", map.Local(3, 0));
        });

        // The path graph needs a moment after the map loads.
        await Pair.RunSeconds(3f);
        await Server.WaitPost(() =>
        {
            var companions = SEntMan.System<CFCompanionSystem>();
            cat = SEntMan.SpawnEntity("CFFamiliarBodyCat", map.Local(1, 0));
            companions.Bind(master, cat);

            // Pointing at an enemy sends a companion at it. A familiar stays put.
            companions.OrderAttack(cat, foe);
        });
        await Pair.RunSeconds(20f);

        var foeHp = 0;
        var ordered = true;
        var distance = 0f;
        await Server.WaitPost(() =>
        {
            foeHp = SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
            ordered = SEntMan.GetComponent<CFCompanionComponent>(cat).Target.IsValid();
            var xform = SEntMan.System<SharedTransformSystem>();
            distance = (xform.GetWorldPosition(cat) - xform.GetWorldPosition(master)).Length();
        });

        Assert.Multiple(() =>
        {
            Assert.That(foeHp, Is.EqualTo(50), "It did not attack.");
            Assert.That(ordered, Is.False, "It took no order to attack.");
            Assert.That(distance, Is.LessThan(4f), "It stayed by its master.");
        });
    }

    private int PetsOf(EntityUid master)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<CFCompanionComponent>();
        while (query.MoveNext(out _, out var companion))
        {
            if (companion.Summoner == master && !string.IsNullOrEmpty(companion.PetId))
                count++;
        }

        return count;
    }

    [Test]
    public async Task AnOrderOrALaterLevelBringsItsPet()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        bool leaf = false, animal = false, storm = true;
        int tooEarly = -1, atLevelTwo = 0, again = 0;
        var peaceful = false;
        await Server.WaitPost(() =>
        {
            var protos = Server.ProtoMan;
            var leafBuild = new CFCharacterBuildData { Class = "Druid" };
            leafBuild.ClassChoices.Add("OrderLeaf");
            var animalBuild = new CFCharacterBuildData { Class = "Druid" };
            animalBuild.ClassChoices.Add("OrderAnimal");
            var stormBuild = new CFCharacterBuildData { Class = "Druid" };
            stormBuild.ClassChoices.Add("OrderStorm");
            leaf = CFPetMath.BuildGrants(protos, leafBuild, CFPetKind.Familiar);
            animal = CFPetMath.BuildGrants(protos, animalBuild, CFPetKind.Companion);
            storm = CFPetMath.BuildGrants(protos, stormBuild, CFPetKind.Familiar) || CFPetMath.BuildGrants(protos, stormBuild, CFPetKind.Companion);

            // A Wizard who takes the Familiar feat in the level 2 slot gets the familiar on reaching level 2, in play.
            var spawn = SEntMan.System<CFCharacterSpawnSystem>();
            var wizard = SEntMan.SpawnEntity("CFFamiliarTestMaster", map.Local(0, 0));
            var build = new CFCharacterBuildData { Class = "Wizard", ClassFeatLevel2 = "CFFamiliarWizard" };
            spawn.EnsureCompanion(wizard, build, 1);
            tooEarly = PetsOf(wizard);
            spawn.EnsureCompanion(wizard, build, 2);
            atLevelTwo = PetsOf(wizard);
            spawn.EnsureCompanion(wizard, build, 3);
            again = PetsOf(wizard);

            var query = SEntMan.AllEntityQueryEnumerator<CFCompanionComponent>();
            while (query.MoveNext(out _, out var companion))
            {
                if (companion.Summoner == wizard)
                    peaceful = companion.Peaceful;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(leaf, Is.True, "The Leaf order gives Leshy Familiar.");
            Assert.That(animal, Is.True, "The Animal order gives Animal Companion.");
            Assert.That(storm, Is.False);
            Assert.That(tooEarly, Is.Zero, "Not before the level its slot belongs to.");
            Assert.That(atLevelTwo, Is.EqualTo(1));
            Assert.That(again, Is.EqualTo(1), "The next level does not bring a second.");
            Assert.That(peaceful, Is.True, "It is a familiar's own body.");
        });
    }
}
