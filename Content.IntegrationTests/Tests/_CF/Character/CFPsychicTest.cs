using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Psychic's minds and feats: feat amps, the Oscillating Wave, Forbidden Thought, Violent Unleash, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFPsiSystem))]
public sealed class CFPsychicTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
# Intelligence 40: a spell DC nothing saves against, and an Occultism check that never fails.
- type: entity
  parent: MobHuman
  id: CFPsychicSage
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    classId: Psychic
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 40
      Wisdom: 0
      Charisma: 0
  - type: CFCombatOptions
  - type: CFExperience
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFPsychicSage
  id: CFPsychicFoe
  components:
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: 0
      Charisma: 0
  - type: CFFaction
    faction: Monster

# Cold does nothing to it, so only the Oscillating Wave's fire turn hurts.
- type: entity
  parent: CFPsychicFoe
  id: CFPsychicColdproofFoe
  components:
  - type: CFResistances
    entries:
    - damageType: Cold
      amount: 999
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFPsiSystem Psi => SEntMan.System<CFPsiSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Spawn(string id = "CFPsychicSage", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Psychic(params string[] minds)
    {
        var hero = Spawn();
        foreach (var name in new[] { "Psi Cantrips and Amps", "Unleash Psyche" })
        {
            var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Psychic")).Features.First(f => f.Name == name);
            Features.Apply(hero, feature.Grants, name);
        }

        foreach (var mind in minds)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(mind).Grants, mind);
        }

        return hero;
    }

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private void Arm(EntityUid hero, string amp = "CFActAmp")
    {
        Actions.PerformAction(hero, Action(hero, amp), new CFActivityInstantEvent());
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    private System.TimeSpan Now => Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;

    /// <summary>Mental Balm takes the place of the cantrip's own amp, and soothes an ally the cantrip reaches.</summary>
    [Test]
    public async Task MentalBalmSoothesAnAlly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var variant = CFAmpVariant.Normal;
        var ownAmp = true;
        int will = 0, frightened = 1, focus = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheInfiniteEye");
            GiveFeat(hero, "CFMentalBalm");
            var ally = Spawn(x: 1);
            SEntMan.System<CFConditionSystem>().ApplyCondition(ally, CFConditionKind.Frightened, 2, 60f, hero, 10);

            Arm(hero, "CFActAmpMentalBalm");
            var guidance = Action(hero, "CFGuidance");
            Psi.NoteCast(hero, guidance);
            variant = Psi.FeatAmp(guidance);
            ownAmp = Psi.Amped(guidance) != null;
            Effects.ResolveTarget(guidance, hero, ally);

            will = Bonuses.Total(ally, CFBonusStat.Will, traits: new() { "Emotion" });
            frightened = SEntMan.GetComponent<CFConditionComponent>(ally).Frightened;
            focus = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusSpent;
        });

        Assert.Multiple(() =>
        {
            Assert.That(variant, Is.EqualTo(CFAmpVariant.MentalBalm));
            Assert.That(ownAmp, Is.False, "The feat's amp replaces the cantrip's own.");
            Assert.That(will, Is.EqualTo(2));
            Assert.That(frightened, Is.EqualTo(0), "The fear is gone.");
            Assert.That(focus, Is.EqualTo(1), "A Focus Point was spent.");
        });
    }

    /// <summary>Homing Beacon: a beaconed creature is seen, and cannot become concealed for a minute.</summary>
    [Test]
    public async Task HomingBeaconKeepsACreatureSeen()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool concealedBefore = false, concealedAfter = true, beaconed = false, hidesAgain = true;
        var variant = CFAmpVariant.Normal;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Psychic("ConsciousTheSilentWhisper");
            GiveFeat(hero, "CFHomingBeacon");
            var foe = Spawn("CFPsychicFoe", 2);
            conditions.ApplyConcealed(foe, 5, breaks: false, 60f);
            concealedBefore = SEntMan.GetComponent<CFConditionComponent>(foe).Concealed;

            var daze = Action(hero, "CFDaze");
            // Intelligence 40: the save fails, so the beacon lands. The flat check to target a concealed creature can miss.
            for (var i = 0; i < 10 && !Psi.IsBeaconed(foe); i++)
            {
                SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusSpent = 0;
                Arm(hero, "CFActAmpHomingBeacon");
                Psi.NoteCast(hero, daze);
                variant = Psi.FeatAmp(daze);
                Effects.ResolveTarget(daze, hero, foe);
            }

            concealedAfter = SEntMan.GetComponent<CFConditionComponent>(foe).Concealed;
            beaconed = Psi.IsBeaconed(foe);
            conditions.ApplyConcealed(foe, 5, breaks: false, 60f);
            hidesAgain = SEntMan.GetComponent<CFConditionComponent>(foe).Concealed;
        });

        Assert.Multiple(() =>
        {
            Assert.That(concealedBefore, Is.True);
            Assert.That(variant, Is.EqualTo(CFAmpVariant.HomingBeacon));
            Assert.That(concealedAfter, Is.False);
            Assert.That(beaconed, Is.True);
            Assert.That(hidesAgain, Is.False, "Nothing conceals it for a minute.");
        });
    }

    /// <summary>The Oscillating Wave: Frostbite is cold, then the next one is fire, which a coldproof foe feels.</summary>
    [Test]
    public async Task TheOscillatingWaveSwapsFireAndCold()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string afterFirst = "", afterSecond = "";
        int firstHurt = 0, secondHurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheOscillatingWave");
            var frostbite = Action(hero, "CFFrostbite");
            var foe = Spawn("CFPsychicColdproofFoe", 2);

            Psi.NoteCast(hero, frostbite);
            afterFirst = Psi.NextEnergy(hero)?.Id ?? "";
            Effects.ResolveTarget(frostbite, hero, foe);
            firstHurt = Hurt(foe);

            Psi.NoteCast(hero, frostbite);
            afterSecond = Psi.NextEnergy(hero)?.Id ?? "";
            Effects.ResolveTarget(frostbite, hero, foe);
            secondHurt = Hurt(foe) - firstHurt;
        });

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo("Fire"), "Cold was removed, so energy is added next.");
            Assert.That(firstHurt, Is.EqualTo(0), "Cold does nothing to it.");
            Assert.That(afterSecond, Is.EqualTo("Cold"));
            Assert.That(secondHurt, Is.GreaterThan(0), "The second Frostbite burned.");
        });
    }

    /// <summary>Forbidden Thought waits on the creature until it Strikes, then bites and is spent.</summary>
    [Test]
    public async Task ForbiddenThoughtBitesOnTheForbiddenStrike()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool locked = false, spentOnStrike = true, castSurvives = false;
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheSilentWhisper");
            var spell = Action(hero, "CFForbiddenThought");
            var foe = Spawn("CFPsychicFoe", 1);
            var strikes = SEntMan.System<CFStrikeSystem>();

            SEntMan.GetComponent<CFModeChoiceComponent>(spell).Chosen = 1;
            Effects.ResolveTarget(spell, hero, foe);
            locked = SEntMan.HasComponent<CFForbiddenThoughtComponent>(foe);

            // Casting is not what was forbidden: the lock stays.
            Psi.CheckLock(foe, CFForbiddenAction.CastSpell);
            castSurvives = SEntMan.HasComponent<CFForbiddenThoughtComponent>(foe);

            strikes.PerformStrike(foe, hero, strikes.UnarmedStats(foe), foe, melee: true);
            spentOnStrike = SEntMan.HasComponent<CFForbiddenThoughtComponent>(foe);
            hurt = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(locked, Is.True);
            Assert.That(castSurvives, Is.True);
            Assert.That(spentOnStrike, Is.False, "The lock went off.");
            Assert.That(hurt, Is.GreaterThan(0), "Intelligence 40: the save fails, 2d6 mental.");
        });
    }

    /// <summary>Violent Unleash has to follow Unleashing at once, and batters everyone near.</summary>
    [Test]
    public async Task ViolentUnleashFollowsTheUnleash()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool allowedCold = true, allowedJust = false;
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheDistantGrasp");
            GiveFeat(hero, "CFViolentUnleash");
            var action = Action(hero, "CFActViolentUnleash");
            var foe = Spawn("CFPsychicFoe", 1);

            var attempt = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
            allowedCold = !attempt.Cancelled;

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).LastSpellCast = Now;
            Actions.PerformAction(hero, Action(hero, "CFActUnleashPsyche"), new CFActivityInstantEvent());
            var again = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref again);
            allowedJust = !again.Cancelled;

            for (var i = 0; i < 10; i++)
            {
                Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            }

            hurt = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(allowedCold, Is.False, "Not Unleashed.");
            Assert.That(allowedJust, Is.True);
            Assert.That(hurt, Is.GreaterThan(0));
        });
    }

    /// <summary>Recall the Teachings Aids every ally within 30 feet at once.</summary>
    [Test]
    public async Task RecallTheTeachingsAidsEveryAlly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int ally = 0, enemy = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("SubconsciousGatheredLore");
            var friend = Spawn(x: 1);
            var foe = Spawn("CFPsychicFoe", 2);
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).LastSpellCast = Now;
            Actions.PerformAction(hero, Action(hero, "CFActUnleashPsyche"), new CFActivityInstantEvent());
            Actions.PerformAction(hero, Action(hero, "CFActRecallTheTeachings"), new CFActivityInstantEvent());
            ally = Bonuses.Total(friend, CFBonusStat.AttackRoll);
            enemy = Bonuses.Total(foe, CFBonusStat.AttackRoll);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ally, Is.AnyOf(1, 2), "Occultism with Intelligence 40 against DC 15.");
            Assert.That(enemy, Is.EqualTo(0));
        });
    }

    /// <summary>Kinetic Ram pushes harder with 2 actions than with 1.</summary>
    [Test]
    public async Task KineticRamPushesByItsActions()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        float one = 0f, two = 0f;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheDistantGrasp");
            var ram = Action(hero, "CFKineticRam");
            var physics = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();

            var first = Spawn("CFPsychicFoe", 2);
            SEntMan.GetComponent<CFModeChoiceComponent>(ram).Chosen = 1;
            Effects.ResolveTarget(ram, hero, first);
            one = SEntMan.GetComponent<PhysicsComponent>(first).LinearVelocity.Length();

            var second = Spawn("CFPsychicFoe", 3);
            SEntMan.GetComponent<CFModeChoiceComponent>(ram).Chosen = 2;
            Effects.ResolveTarget(ram, hero, second);
            two = SEntMan.GetComponent<PhysicsComponent>(second).LinearVelocity.Length();
            physics.SetLinearVelocity(first, System.Numerics.Vector2.Zero);
            physics.SetLinearVelocity(second, System.Numerics.Vector2.Zero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.GreaterThan(0f).And.LessThanOrEqualTo(24.1f), "10 feet, or 20 on a critical failure.");
            Assert.That(two, Is.GreaterThanOrEqualTo(23.9f), "20 feet, or 40 on a critical failure.");
        });
    }

    /// <summary>Mental Buffer is half the level, and the whole level while Unleashed.</summary>
    [Test]
    public async Task MentalBufferGrowsWhileUnleashed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int calm = 0, unleashed = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic();
            SEntMan.GetComponent<CFCombatantComponent>(hero).Level = 4;
            GiveFeat(hero, "CFMentalBuffer");
            calm = Bonuses.ModifyDamage(hero, "Mental", 10);
            SEntMan.EnsureComponent<CFUnleashedComponent>(hero).Until = Now + System.TimeSpan.FromSeconds(60);
            unleashed = Bonuses.ModifyDamage(hero, "Mental", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.EqualTo(8), "Resistance 2 at level 4.");
            Assert.That(unleashed, Is.EqualTo(6), "Resistance 4 while Unleashed.");
        });
    }

    /// <summary>Psychic Rapport: a mental spell opens the mind to others.</summary>
    [Test]
    public async Task PsychicRapportFollowsAMentalSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = -1, after = 0, other = -1;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheSilentWhisper");
            GiveFeat(hero, "CFPsychicRapport");
            before = Bonuses.Total(hero, CFBonusStat.SkillChecks, skill: "Deception");
            Psi.NoteCast(hero, Action(hero, "CFDaze"));
            after = Bonuses.Total(hero, CFBonusStat.SkillChecks, skill: "Deception");
            other = Bonuses.Total(hero, CFBonusStat.SkillChecks, skill: "Athletics");
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(after, Is.EqualTo(1));
            Assert.That(other, Is.EqualTo(0), "Only the three social skills.");
        });
    }

    /// <summary>Warp Step is +5 feet, and +10 amped.</summary>
    [Test]
    public async Task WarpStepIsFasterAmped()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, amped = 0;
        await Server.WaitPost(() =>
        {
            var hero = Psychic("ConsciousTheUnboundStep");
            var step = Action(hero, "CFWarpStep");
            Psi.NoteCast(hero, step);
            Effects.ResolveTarget(step, hero, hero);
            plain = Bonuses.Total(hero, CFBonusStat.Speed);

            var other = Psychic("ConsciousTheUnboundStep");
            var otherStep = Action(other, "CFWarpStep");
            Arm(other);
            Psi.NoteCast(other, otherStep);
            Effects.ResolveTarget(otherStep, other, other);
            amped = Bonuses.Total(other, CFBonusStat.Speed);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(5));
            Assert.That(amped, Is.EqualTo(10));
        });
    }

    /// <summary>Every mind's grants apply, and each conscious mind gives its new spell.</summary>
    [Test]
    public async Task EveryMindGivesItsSpells()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var missing = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var (mind, spell) in new[]
                     {
                         ("ConsciousTheDistantGrasp", "CFKineticRam"),
                         ("ConsciousTheSilentWhisper", "CFForbiddenThought"),
                         ("ConsciousTheUnboundStep", "CFWarpStep"),
                         ("ConsciousTheInfiniteEye", "CFGuidance"),
                         ("ConsciousTheTangibleDream", "CFShield"),
                         ("ConsciousTheOscillatingWave", "CFFrostbite"),
                     })
            {
                var hero = Psychic(mind);
                if (Actions.GetActions(hero).All(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID != spell))
                    missing.Add($"{mind}: {spell}");
            }
        });

        Assert.That(missing, Is.Empty);
    }
}
