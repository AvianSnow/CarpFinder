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
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Psi amps and Unleash Psyche, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFPsiSystem))]
public sealed class CFPsiTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFPsiHero
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
  parent: CFPsiHero
  id: CFPsiFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFPsiSystem Psi => SEntMan.System<CFPsiSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private EntityUid Spawn(string id = "CFPsiHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Psychic()
    {
        var hero = Spawn();
        foreach (var name in new[] { "Psi Cantrips and Amps", "Unleash Psyche" })
        {
            var feature = Server.ProtoMan.Index<CFClassPrototype>("Psychic").Features.First(f => f.Name == name);
            Features.Apply(hero, feature.Grants, name);
        }

        return hero;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private int Remaining(EntityUid hero) => CFFocusSystem.Remaining(SEntMan.GetComponent<CFSpellbookComponent>(hero));

    private void Arm(EntityUid hero) => Actions.PerformAction(hero, Action(hero, "CFActAmp"), new CFActivityInstantEvent());

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    private static EntityUid Spell(IEntityManager entities, string id) => entities.SpawnEntity(id, MapCoordinates.Nullspace);

    [Test]
    public async Task AnAmpSpendsAFocusPointOnlyWhenArmed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int start = 0, unarmed = 0, armed = 0;
        bool plain = true, amped = false;
        await Server.WaitPost(() =>
        {
            var hero = Psychic();
            var spell = Spell(SEntMan, "CFDaze");
            start = Remaining(hero);
            Psi.NoteCast(hero, spell);
            unarmed = Remaining(hero);
            plain = Psi.Amped(spell) != null;
            Arm(hero);
            Psi.NoteCast(hero, spell);
            armed = Remaining(hero);
            amped = Psi.Amped(spell) != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(2));
            Assert.That(unarmed, Is.EqualTo(2));
            Assert.That(plain, Is.False);
            Assert.That(armed, Is.EqualTo(1));
            Assert.That(amped, Is.True);
        });
    }

    [Test]
    public async Task WithNoFocusThereIsNoAmp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var amped = true;
        await Server.WaitPost(() =>
        {
            var hero = Psychic();
            var spell = Spell(SEntMan, "CFDaze");
            SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusSpent = 2;
            Arm(hero);
            Psi.NoteCast(hero, spell);
            amped = Psi.Amped(spell) != null;
        });

        Assert.That(amped, Is.False);
    }

    /// <summary>Daze deals only the caster's ability modifier, 0 here. Amped it rolls a d10.</summary>
    [Test]
    public async Task AmpedDazeHitsHarder()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, amped = 0;
        await Server.WaitPost(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            var hero = Psychic();
            var spell = Spell(SEntMan, "CFDaze");
            for (var i = 0; i < 30; i++)
            {
                var foe = Spawn("CFPsiFoe", 1);
                effects.ResolveTarget(spell, hero, foe);
                plain = System.Math.Max(plain, Hurt(foe));
            }

            SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusSpent = -30;
            for (var i = 0; i < 30; i++)
            {
                Arm(hero);
                Psi.NoteCast(hero, spell);
                var foe = Spawn("CFPsiFoe", 1);
                effects.ResolveTarget(spell, hero, foe);
                amped = System.Math.Max(amped, Hurt(foe));
                SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ReactionSpent = 0;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.LessThanOrEqualTo(1));
            Assert.That(amped, Is.GreaterThan(1));
        });
    }

    [Test]
    public async Task UnleashNeedsARecentSpellAndLeavesBacklash()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        bool before = true, after = false, unleashed = false, again = true, gone = true;
        var stupefied = 0;
        await Server.WaitPost(() =>
        {
            hero = Psychic();
            var action = Action(hero, "CFActUnleashPsyche");
            before = Psi.CanUnleash(hero);
            Psi.NoteCast(hero, Spell(SEntMan, "CFDaze"));
            after = Psi.CanUnleash(hero);
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            unleashed = SEntMan.HasComponent<CFUnleashedComponent>(hero);
            again = Psi.CanUnleash(hero);
            SEntMan.GetComponent<CFUnleashedComponent>(hero).Until = Server.ResolveDependency<IGameTiming>().CurTime;
        });
        await Pair.RunTicksSync(3);
        await Server.WaitPost(() =>
        {
            gone = !SEntMan.HasComponent<CFUnleashedComponent>(hero);
            stupefied = SEntMan.System<CFConditionSystem>().Stupefied(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
            Assert.That(unleashed, Is.True);
            Assert.That(again, Is.False);
            Assert.That(gone, Is.True);
            Assert.That(stupefied, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PsycheActionsNeedAnUnleashedPsyche()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool calm = true, unleashed = false;
        await Server.WaitPost(() =>
        {
            var hero = Psychic();
            var action = Spell(SEntMan, "CFActCalculateThreats");
            var first = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref first);
            calm = !first.Cancelled;
            SEntMan.EnsureComponent<CFUnleashedComponent>(hero);
            var second = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref second);
            unleashed = !second.Cancelled;
        });

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.False);
            Assert.That(unleashed, Is.True);
        });
    }

    [Test]
    public async Task EveryMindCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Psychic"))
            {
                Features.Apply(Spawn(), choice.Grants, choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(10));
    }
}
