using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Magus through Runesmith, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFCharacterFeatureSystem))]
public sealed class CFWave5Test : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);
    private static readonly string[] Classes = { "Magus", "Thaumaturge", "Gunslinger", "Inventor", "Psychic", "Commander", "Guardian", "Necromancer", "Animist", "Exemplar", "Runesmith" };
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWave5Hero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFWave5Hero
  id: CFWave5Foe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id = "CFWave5Hero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void GiveFeature(EntityUid hero, string cls, string name)
    {
        var feature = Server.ProtoMan.Index<CFClassPrototype>(cls).Features.First(f => f.Name == name);
        Features.Apply(hero, feature.Grants, $"feature:{name}");
    }

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>(id).Grants, $"feat:{id}");
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    [Test]
    public async Task ArcaneCascadeAddsMeleeDamage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, during = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Magus", "Arcane Cascade");
            var bonuses = SEntMan.System<CFBonusSystem>();
            before = bonuses.Total(hero, CFBonusStat.MeleeDamage);
            SEntMan.System<SharedActionsSystem>().PerformAction(hero, Action(hero, "CFActArcaneCascade"), new CFActivityInstantEvent());
            during = bonuses.Total(hero, CFBonusStat.MeleeDamage);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(during, Is.EqualTo(1));
        });
    }

    /// <summary>At level 1 the most a Reflex success can leave is 4, and a critical failure 8.</summary>
    [Test]
    public async Task PsiBurstDealsDamageWithASave()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var most = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFPsiBurst");
            var action = Action(hero, "CFActPsiBurst");
            var actions = SEntMan.System<SharedActionsSystem>();
            var effects = SEntMan.System<CFEffectSystem>();
            for (var i = 0; i < 40; i++)
            {
                var foe = Spawn("CFWave5Foe", 1);
                effects.ResolveTarget(action, hero, foe);
                most = System.Math.Max(most, Hurt(foe));
            }
        });

        Assert.That(most, Is.InRange(1, 8));
    }

    [Test]
    public async Task GuardianArmorNeedsMediumOrHeavyArmor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int bare = 0, armored = 0, fire = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Guardian", "Guardian's Armor");
            var bonuses = SEntMan.System<CFBonusSystem>();
            bare = bonuses.ModifyDamage(hero, "Slashing", 10);
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(hero, Spawn("CFBreastplate"), "outerClothing", force: true), Is.True);
            armored = bonuses.ModifyDamage(hero, "Slashing", 10);
            fire = bonuses.ModifyDamage(hero, "Fire", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(bare, Is.EqualTo(10));
            Assert.That(armored, Is.EqualTo(9), "1 + half of level 1, rounded down.");
            Assert.That(fire, Is.EqualTo(10), "Only physical damage.");
        });
    }

    [Test]
    public async Task RankFeaturesRaiseRanks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank reflex = default, unarmed = default, crossbow = default;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Magus", "Reflex Expertise");
            GiveFeature(hero, "Guardian", "Weapon Expertise");
            GiveFeature(hero, "Gunslinger", "Slinger's Precision");
            var ranks = SEntMan.GetComponent<CFCombatantComponent>(hero).ProficiencyRanks;
            reflex = ranks["Reflex"];
            unarmed = ranks["Unarmed"];
            crossbow = ranks["Crossbow"];
        });

        Assert.Multiple(() =>
        {
            Assert.That(reflex, Is.EqualTo(CFProficiencyRank.Expert));
            Assert.That(unarmed, Is.EqualTo(CFProficiencyRank.Expert));
            Assert.That(crossbow, Is.EqualTo(CFProficiencyRank.Expert));
        });
    }

    [Test]
    public async Task ASubconsciousMindSetsTheKeyAttribute()
    {
        await Server.WaitAssertion(() =>
        {
            var psychic = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Psychic"));
            var build = new CFCharacterBuildData { Class = "Psychic" };
            Assert.That(CFClassChoices.KeyAbility(Server.ProtoMan, build, psychic), Is.EqualTo(CFAbility.Intelligence));
            build.ClassChoices.Add("SubconsciousEmotionalAcceptance");
            Assert.That(CFClassChoices.KeyAbility(Server.ProtoMan, build, psychic), Is.EqualTo(CFAbility.Charisma));
        });
    }

    /// <summary>A grant that names a missing action or spell only fails when it runs.</summary>
    [Test]
    public async Task EveryFeatAndChoiceCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int feats = 0, choices = 0;
        await Server.WaitPost(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class != null && Classes.Contains(f.Class)))
            {
                GiveFeat(Spawn(), feat.ID);
                feats++;
            }

            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => Classes.Contains(c.Class.Id)))
            {
                Features.Apply(Spawn(), choice.Grants, $"choice:{choice.ID}");
                choices++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(feats, Is.GreaterThanOrEqualTo(190));
            Assert.That(choices, Is.GreaterThanOrEqualTo(50));
        });
    }

    [Test]
    public async Task EveryRequiredSubclassExistsForItsClass()
    {
        var wrong = new List<string>();
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class != null && Classes.Contains(f.Class)))
            {
                foreach (var id in feat.RequiresChoice)
                {
                    if (!Server.ProtoMan.TryIndex(id, out var choice) || choice.Class.Id != feat.Class)
                        wrong.Add($"{feat.ID}: {id}");
                }
            }
        });

        Assert.That(wrong, Is.Empty);
    }
}
