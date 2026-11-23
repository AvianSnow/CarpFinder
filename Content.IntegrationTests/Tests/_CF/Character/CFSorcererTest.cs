using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Sorcerer: blood magic for each bloodline, the bloodline spells, Bleed Out and Jealous Hex.</summary>
[TestFixture]
[TestOf(typeof(CFBloodMagicSystem))]
public sealed class CFSorcererTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFClassChoicePrototype> Demonic = "BloodlineDemonic";
    private static readonly ProtoId<CFClassChoicePrototype> Diabolic = "BloodlineDiabolic";
    private static readonly ProtoId<CFClassChoicePrototype> Angelic = "BloodlineAngelic";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSorcererTestHero
  components:
  - type: CFCombatant
    level: 5
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 3
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFSorcererTestHero
  id: CFSorcererTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFBloodMagicSystem Blood => SEntMan.System<CFBloodMagicSystem>();

    private EntityUid Spawn(string id = "CFSorcererTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Sorcerer(ProtoId<CFClassChoicePrototype> bloodline)
    {
        var hero = Spawn();
        SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, Server.ProtoMan.Index(bloodline).Grants, $"choice:{bloodline.Id}");
        return hero;
    }

    private Entity<CFSpellComponent> Spell(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        var action = SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == id);
        return (action.Owner, SEntMan.GetComponent<CFSpellComponent>(action.Owner));
    }

    [Test]
    public async Task EveryBloodlineHasBloodMagic()
    {
        await Server.WaitAssertion(() =>
        {
            var seen = 0;
            foreach (var bloodline in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>())
            {
                if (bloodline.Class != "Sorcerer")
                    continue;

                seen++;
                var blood = bloodline.Grants.OfType<CFGrantBloodMagic>().SingleOrDefault();
                Assert.That(blood, Is.Not.Null, bloodline.ID);
                Assert.That(blood.OnSelf, Is.Not.Empty, bloodline.ID);
                foreach (var spell in blood.Spells)
                {
                    Assert.That(Server.ProtoMan.HasIndex(spell), $"{bloodline.ID}: no cfSpell {spell}");
                    Assert.That(Server.ProtoMan.HasIndex<EntityPrototype>("CF" + spell.Id), $"{bloodline.ID}: no action for {spell}");
                }
            }

            Assert.That(seen, Is.EqualTo(13));
        });
    }

    [Test]
    public async Task BloodMagicGoesToTheEnemyOrElseTheCaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int foeAc = 0, selfSkill = 0, cantrip = 0, burned = 0, allySaves = 0, casterSaves = 0;
        bool recent = false;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();

            // Demonic: an enemy the spell reaches loses 1 AC.
            var demon = Sorcerer(Demonic);
            var fear = Spell(demon, "CFFear");
            var foe = Spawn("CFSorcererTestFoe", 1);
            Blood.Begin(demon, fear);
            Blood.NoteTarget(demon, fear, foe);
            Blood.Finish(demon);
            foeAc = bonuses.TotalOfType(foe, CFBonusStat.ArmorClass, CFBonusType.Status);
            recent = Blood.Recent(demon);

            // The same bloodline with nobody to hinder: Intimidation for the caster.
            var alone = Sorcerer(Demonic);
            var again = Spell(alone, "CFFear");
            Blood.Begin(alone, again);
            Blood.Finish(alone);
            selfSkill = bonuses.Total(alone, CFBonusStat.SkillChecks, skill: "Intimidation");

            // A cantrip, or a spell that is not the bloodline's, does nothing.
            var quiet = Sorcerer(Demonic);
            Blood.Begin(quiet, Spell(quiet, "CFDaze"));
            Blood.Finish(quiet);
            cantrip = bonuses.Total(quiet, CFBonusStat.SkillChecks, skill: "Intimidation");

            // Diabolic: 1 fire damage per rank. A level 5 caster's focus spell is rank 3.
            var devil = Sorcerer(Diabolic);
            var edict = Spell(devil, "CFDiabolicEdict");
            var victim = Spawn("CFSorcererTestFoe", 1);
            Blood.Begin(devil, edict);
            Blood.NoteTarget(devil, edict, victim);
            burned = StartHp - SEntMan.GetComponent<CFCombatantComponent>(victim).CurrentHp;

            // Angelic: cast on an ally, the ally is the one protected.
            var angel = Sorcerer(Angelic);
            var halo = Spell(angel, "CFAngelicHalo");
            var friend = Spawn(x: 1);
            Blood.Begin(angel, halo);
            Blood.NoteTarget(angel, halo, friend);
            Blood.Finish(angel);
            allySaves = bonuses.TotalOfType(friend, CFBonusStat.Saves, CFBonusType.Status);
            casterSaves = bonuses.TotalOfType(angel, CFBonusStat.Saves, CFBonusType.Status);
        });

        Assert.Multiple(() =>
        {
            Assert.That(foeAc, Is.EqualTo(-1));
            Assert.That(recent, Is.True, "Bleed Out can follow");
            Assert.That(selfSkill, Is.EqualTo(2));
            Assert.That(cantrip, Is.EqualTo(0));
            Assert.That(burned, Is.EqualTo(3));
            Assert.That(allySaves, Is.EqualTo(1));
            Assert.That(casterSaves, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task JealousHexTakesTheHighestAttribute()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int enfeebled = 0, stupefied = -1;
        await Server.WaitPost(() =>
        {
            // The test hero's best attribute is Strength.
            var hero = Spawn();
            var target = Spawn("CFSorcererTestFoe", 1);
            SEntMan.System<CFEffectSystem>().Run(new List<CFEffect> { new CFJealousHexEffect { Value = 2 } }, new CFEffectArgs
            {
                User = hero,
                Source = hero,
                Target = target,
            });
            var conditions = SEntMan.System<CFConditionSystem>();
            enfeebled = conditions.Value(target, CFConditionKind.Enfeebled);
            stupefied = conditions.Stupefied(target);
        });

        Assert.Multiple(() =>
        {
            Assert.That(enfeebled, Is.EqualTo(2));
            Assert.That(stupefied, Is.EqualTo(0));
        });
    }
}
