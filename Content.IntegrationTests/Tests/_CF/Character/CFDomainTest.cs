using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Deities' domains, the feats that pick one, and the domain spells that need more than an effect list.</summary>
[TestFixture]
[TestOf(typeof(CFDomainPrototype))]
public sealed class CFDomainTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFDomainTestHero
  components:
  - type: CFCombatant
    level: 3
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFDomainTestHero
  id: CFDomainTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(int x, string id = "CFDomainTestHero")
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Cleric(string deity, string domain)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Class = "Cleric", Deity = deity };
        build.ClassChoices.Add("DoctrineCloistered");
        build.OptionPicks.Add(new CFOptionPick { Source = "CFDomainInitiate", Slot = 0, Value = domain });
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCFBuild(build));
        return hero;
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    /// <summary>Gives a domain's grants straight to a creature, with a Focus Point to spend.</summary>
    private void GiveDomain(EntityUid mob, string domain)
    {
        var grants = Server.ProtoMan.Index<CFDomainPrototype>(domain).Grants;
        SEntMan.System<CFCharacterFeatureSystem>().Apply(mob, grants, $"domain:{domain}");
    }

    private int FocusLeft(EntityUid mob)
    {
        return Content.Shared.CF.Spells.CFFocusSystem.Remaining(SEntMan.GetComponent<CFSpellbookComponent>(mob));
    }

    [Test]
    public async Task EveryDomainExistsAndGivesItsSpell()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var deity in Server.ProtoMan.EnumeratePrototypes<CFDeityPrototype>())
            {
                if (deity.ID.Contains("Test"))
                    continue;

                Assert.That(deity.Domains, Has.Count.EqualTo(3), deity.ID);
                foreach (var domain in deity.Domains)
                {
                    Assert.That(Server.ProtoMan.HasIndex(domain), $"{deity.ID} names {domain}");
                }
            }

            foreach (var domain in Server.ProtoMan.EnumeratePrototypes<CFDomainPrototype>())
            {
                var text = Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString(domain.Description);
                Assert.That(domain.Grants.Count > 0 || text.Contains("TBA"), $"{domain.ID} gives nothing and does not say TBA");
                foreach (var grant in domain.Grants.OfType<Content.Shared.CF.Character.Grants.CFGrantFocusSpell>())
                {
                    Assert.That(Server.ProtoMan.HasIndex<EntityPrototype>("CF" + grant.Spell.Id), $"{domain.ID}: no action for {grant.Spell.Id}");
                    Assert.That(Server.ProtoMan.HasIndex(grant.Spell), $"{domain.ID}: no cfSpell {grant.Spell.Id}");
                }
            }
        });
    }

    [Test]
    public async Task ACloisteredClericGetsTheDomainSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool sun = false, foreign = true;
        await Server.WaitPost(() =>
        {
            sun = HasAction(Cleric("Solhara", "Sun"), "CFDazzlingFlash");

            // Death is not one of Solhara's domains, so the pick is dropped.
            foreign = HasAction(Cleric("Solhara", "Death"), "CFDazzlingFlash");
        });

        Assert.Multiple(() =>
        {
            Assert.That(sun, Is.True);
            Assert.That(foreign, Is.False);
        });
    }

    [Test]
    public async Task SplinterFaithReplacesTheDeitysDomains()
    {
        await Server.WaitAssertion(() =>
        {
            var choice = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFDomainInitiate")).Choices[0];
            var build = new CFCharacterBuildData { Class = "Cleric", Deity = "Solhara" };
            Assert.That(CFOptionChoices.Allowed(Server.ProtoMan, choice, build), Is.EquivalentTo(new[] { "Sun", "Healing", "Repose" }));

            foreach (var (domain, slot) in new[] { ("Air", 0), ("Death", 1), ("Luck", 2), ("Might", 3) })
            {
                build.OptionPicks.Add(new CFOptionPick { Source = "CFSplinterFaithCleric", Slot = slot, Value = domain });
            }

            Assert.That(CFOptionChoices.Allowed(Server.ProtoMan, choice, build), Is.EquivalentTo(new[] { "Air", "Death", "Luck", "Might" }));
        });
    }

    [Test]
    public async Task ProtectorsSacrificeTakesPartOfTheDamage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int left = 0, protectorHp = 0, focus = -1;
        await Server.WaitPost(() =>
        {
            var protector = Spawn(0);
            GiveDomain(protector, "Protection");
            var ally = Spawn(1);
            var foe = Spawn(2, "CFDomainTestFoe");

            left = SEntMan.System<CFReactionSystem>().AnswerDamage(ally, foe, 10, "Slashing");
            protectorHp = SEntMan.GetComponent<CFCombatantComponent>(protector).CurrentHp;
            focus = FocusLeft(protector);
        });

        // Level 3 is rank 2, so 6 of the 10 go to the protector.
        Assert.Multiple(() =>
        {
            Assert.That(left, Is.EqualTo(4));
            Assert.That(protectorHp, Is.EqualTo(494));
            Assert.That(focus, Is.EqualTo(0), "The reaction spends the Focus Point.");
        });
    }

    [Test]
    public async Task SuddenShiftAndDeathsCall()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool concealed = false;
        int tempHp = 0;
        await Server.WaitPost(() =>
        {
            var trickster = Spawn(0);
            GiveDomain(trickster, "Trickery");
            var foe = Spawn(1, "CFDomainTestFoe");
            SEntMan.System<CFReactionSystem>().AnswerMiss(trickster, foe);
            concealed = SEntMan.GetComponent<CFConditionComponent>(trickster).Concealed;

            var reaper = Spawn(0);
            GiveDomain(reaper, "Death");
            var victim = Spawn(1, "CFDomainTestFoe");
            SEntMan.System<CFDyingSystem>().Kill(victim);
            tempHp = SEntMan.GetComponentOrNull<CFCombatantComponent>(reaper)?.TemporaryHp ?? 0;
        });

        Assert.Multiple(() =>
        {
            Assert.That(concealed, Is.True);
            Assert.That(tempHp, Is.EqualTo(3), "The dead creature's level, plus a spellcasting modifier of 0.");
        });
    }

    [Test]
    public async Task ShareBurdenLuckAndHalvedHealing()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree emotion = default, plain = default;
        bool luckOnce = false, luckTwice = true;
        int healed = 0;
        await Server.WaitPost(() =>
        {
            var priest = Spawn(0);
            GiveDomain(priest, "Repose");
            var ally = Spawn(1);
            var total = 0;
            plain = SEntMan.System<CFReactionSystem>().AnswerSave(ally, CFDefenseType.Will, 5, 0, 15, ref total, CFDegree.Failure, new HashSet<ProtoId<CFTraitPrototype>>());
            emotion = SEntMan.System<CFReactionSystem>().AnswerSave(ally, CFDefenseType.Will, 5, 0, 15, ref total, CFDegree.Failure, new HashSet<ProtoId<CFTraitPrototype>> { "Emotion" });

            var bonuses = SEntMan.System<CFBonusSystem>();
            bonuses.GrantSaveFortune(ally, 60f);
            luckOnce = bonuses.TryConsumeSaveFortune(ally);
            luckTwice = bonuses.TryConsumeSaveFortune(ally);

            var hurt = Spawn(2);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hurt);
            combat.CurrentHp = 100;
            SEntMan.System<CFConditionSystem>().ApplyCondition(hurt, CFConditionKind.HealingHalved, 1, 60f, priest, 0);
            SEntMan.System<CFEffectSystem>().Heal(priest, hurt, 10);
            healed = combat.CurrentHp - 100;
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(CFDegree.Failure), "Share Burden only answers emotion effects.");
            Assert.That(emotion, Is.EqualTo(CFDegree.Success));
            Assert.That(luckOnce, Is.True);
            Assert.That(luckTwice, Is.False, "Bit of Luck is used up by one save.");
            Assert.That(healed, Is.EqualTo(5));
        });
    }
}
