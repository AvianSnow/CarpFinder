using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Cleric, Wizard, Bard, Druid and Witch feats, and feats that need a subclass, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFClassChoices))]
public sealed class CFWave3Test : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWave3Hero
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
  parent: CFWave3Hero
  id: CFWave3Foe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFSpellInstant
  id: CFWave3TestCast
  components:
  - type: CFSpell
    spell: Shield
";

    private CFDevMap _map = default!;

    private EntityUid Hero(params string[] feats)
    {
        var hero = SEntMan.SpawnEntity("CFWave3Hero", _map.Local(0, 0));
        foreach (var id in feats)
        {
            SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>(id).Grants, $"feat:{id}");
        }

        return hero;
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private static HashSet<ProtoId<CFTraitPrototype>> Traits(string trait)
    {
        return new HashSet<ProtoId<CFTraitPrototype>> { trait };
    }

    private int MostHealed(EntityUid hero)
    {
        var effects = SEntMan.System<CFEffectSystem>();
        var spell = SEntMan.SpawnEntity("CFHeal", MapCoordinates.Nullspace);
        var patient = SEntMan.SpawnEntity("CFWave3Hero", _map.Local(0, 0));
        var combat = SEntMan.GetComponent<CFCombatantComponent>(patient);
        var most = 0;
        for (var i = 0; i < 80; i++)
        {
            combat.CurrentHp = 100;
            effects.ResolveTarget(spell, hero, patient);
            most = Math.Max(most, combat.CurrentHp - 100);
        }

        return most;
    }

    /// <summary>Heal at rank 1 is 1d8+8. With d10s, a 17 or 18 is bound to come up in this many casts.</summary>
    [Test]
    public async Task HealingHandsRollsD10s()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, hands = 0;
        await Server.WaitPost(() =>
        {
            plain = MostHealed(Hero());
            hands = MostHealed(Hero("CFHealingHands"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.LessThanOrEqualTo(16));
            Assert.That(hands, Is.GreaterThan(16));
        });
    }

    /// <summary>The watcher has Reactive Strike. A cast sets it off, unless the spell was concealed.</summary>
    [Test]
    public async Task ConcealSpellDoesNotSetOffReactions()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int open = 0, hidden = -1;
        await Server.WaitPost(() =>
        {
            var actions = SEntMan.System<SharedActionsSystem>();
            int Watch(params string[] before)
            {
                var caster = Hero();
                var watcher = SEntMan.SpawnEntity("CFWave3Foe", _map.Local(1, 0));
                var options = SEntMan.GetComponent<CFCombatOptionsComponent>(watcher);
                options.ReactiveStrike = true;
                foreach (var id in before)
                {
                    actions.PerformAction(caster, Grant(caster, id));
                }

                actions.PerformAction(caster, Grant(caster, "CFWave3TestCast"));
                return options.ReactionSpent;
            }

            open = Watch();
            hidden = Watch("CFActConcealSpell");
        });

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.Not.Zero);
            Assert.That(hidden, Is.Zero);
        });
    }

    [Test]
    public async Task WellVersedAndPoisonResistance()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int versed = 0, poison = 0, damage = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var bard = Hero("CFWellVersed");
            versed = conditions.DefenseModifier(bard, CFDefenseType.Will, Traits("Sonic")) - conditions.DefenseModifier(bard, CFDefenseType.Will);
            var druid = Hero("CFPoisonResistanceDruid");
            poison = conditions.DefenseModifier(druid, CFDefenseType.Fortitude, Traits("Poison")) - conditions.DefenseModifier(druid, CFDefenseType.Fortitude);
            damage = SEntMan.System<CFBonusSystem>().ModifyDamage(druid, "Poison", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(versed, Is.EqualTo(1));
            Assert.That(poison, Is.EqualTo(1));
            Assert.That(damage, Is.EqualTo(9));
        });
    }

    [Test]
    public async Task CantripExpansionOffersItsOwnSpellList()
    {
        var count = 0;
        var offered = new List<string>();
        var divine = new List<string>();
        await Server.WaitPost(() =>
        {
            var choice = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFCantripExpansionWizard")).Choices[0];
            count = choice.Count;
            offered = CFOptionChoices.Allowed(Server.ProtoMan, choice);
            divine = Server.ProtoMan.EnumeratePrototypes<CFSpellPrototype>()
                .Where(spell => spell.Rank == 0 && !spell.HasTradition(CFSpellTradition.Arcane))
                .Select(spell => spell.ID)
                .ToList();
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(offered, Is.Not.Empty);
            Assert.That(offered.Intersect(divine), Is.Empty);
        });
    }

    [Test]
    public async Task AFeatThatNeedsASubclassNeedsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool ruffian = false, thief = true;
        string spawned = "x", kept = null;
        ProtoId<CFFeatPrototype>? dropped = "CFBrutalBeating";
        await Server.WaitPost(() =>
        {
            var feat = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFBrutalBeating"));
            var asRuffian = new CFCharacterBuildData { Class = "Rogue", ClassFeatLevel2 = "CFBrutalBeating" };
            asRuffian.ClassChoices.Add("Ruffian");
            var asThief = new CFCharacterBuildData { Class = "Rogue", ClassFeatLevel2 = "CFBrutalBeating" };
            asThief.ClassChoices.Add("Thief");

            ruffian = CFClassChoices.MeetsRequirement(Server.ProtoMan, asRuffian, feat);
            thief = CFClassChoices.MeetsRequirement(Server.ProtoMan, asThief, feat);

            CFExperienceComponent Spawned(CFCharacterBuildData build)
            {
                var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
                SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
                return SEntMan.GetComponent<CFExperienceComponent>(hero);
            }

            spawned = Spawned(asThief).ClassFeatLevel2;
            kept = Spawned(asRuffian).ClassFeatLevel2;

            CFClassChoices.Sanitize(Server.ProtoMan, asThief);
            dropped = asThief.ClassFeatLevel2;
        });

        Assert.Multiple(() =>
        {
            Assert.That(ruffian, Is.True);
            Assert.That(thief, Is.False);
            Assert.That(spawned, Is.Null, "A client can send a feat its build does not qualify for.");
            Assert.That(kept, Is.EqualTo("CFBrutalBeating"));
            Assert.That(dropped, Is.Null);
        });
    }

    [Test]
    public async Task EveryWaveThreeFeatWorksOrSaysTba()
    {
        var unmarked = new List<string>();
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            string[] classes = { "Cleric", "Wizard", "Bard", "Druid", "Witch" };
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(feat => feat.Class is { } id && classes.Contains(id)))
            {
                var text = loc.GetString(feat.Description);
                // The companion system goes by the feat itself, so this one works with no grants.
                var works = feat.ID == "CFAnimalCompanion" || feat.Grants.Count > 0 || feat.Choices.Count > 0 || feat.TrainedSkills.Count > 0 || feat.Lores.Count > 0;
                if (!works && !text.Contains("TBA") && !text.Contains("implemented later") && !text.Contains("ot applied in play"))
                    unmarked.Add(feat.ID);
            }
        });

        Assert.That(unmarked, Is.Empty);
    }
}
