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
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Recharging Spellstrike, Taunt and Intercept Attack, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFTauntSystem))]
public sealed class CFGuardianMagusTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);
    private const int FarAway = 7;
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGuardMagusHero
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
  parent: CFGuardMagusHero
  id: CFGuardMagusFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFTauntSystem Taunts => SEntMan.System<CFTauntSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private EntityUid Spawn(string id = "CFGuardMagusHero", int x = 0)
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
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool CanUse(EntityUid hero, EntityUid action, EntityUid target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    [Test]
    public async Task SpellstrikeHasToBeRecharged()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool ready = false, spent = true, recharged = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFGuardMagusFoe", 1);
            GiveFeature(hero, "Magus", "Spellstrike");
            var strike = Action(hero, "CFActSpellstrike");

            ready = CanUse(hero, strike, foe);
            Actions.PerformAction(hero, strike, new CFActivityTargetEvent { Target = foe });
            spent = CanUse(hero, strike, foe);
            Actions.PerformAction(hero, Action(hero, "CFActSpellstrikeRecharge"), new CFActivityInstantEvent());
            recharged = CanUse(hero, strike, foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ready, Is.True);
            Assert.That(spent, Is.False);
            Assert.That(recharged, Is.True);
        });
    }

    [Test]
    public async Task AConfluxSpellRechargesSpellstrike()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var spentAfter = true;
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFGuardMagusFoe", 1);
            GiveFeature(hero, "Magus", "Spellstrike");
            GiveFeat(hero, "CFForceFang");
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.SpellstrikeSpent = true;
            Actions.PerformAction(hero, Action(hero, "CFForceFang"), new CFSpellCastTargetEvent { Target = foe });
            spentAfter = options.SpellstrikeSpent;
            hurt = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(spentAfter, Is.False);
            Assert.That(hurt, Is.InRange(2, 5));
        });
    }

    [Test]
    public async Task ATauntPunishesIgnoringTheTaunter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int ally = 1, taunter = 1, friend = 1;
        bool offGuard = false, ignored = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var mate = Spawn(x: 1);
            var foe = Spawn("CFGuardMagusFoe", 1);
            var foeFriend = Spawn("CFGuardMagusFoe", 2);
            Taunts.Taunt(hero, foe);

            taunter = Taunts.NoteAttack(foe, hero);
            friend = Taunts.NoteAttack(foe, foeFriend);
            ignored = Taunts.HasIgnored(foe, hero);
            ally = Taunts.NoteAttack(foe, mate);
            offGuard = SEntMan.System<CFConditionSystem>().IsOffGuardTo(foe, hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(taunter, Is.EqualTo(0), "Attacking the taunter is what it wants.");
            Assert.That(friend, Is.EqualTo(0), "Only the taunter's allies count.");
            Assert.That(ignored, Is.False);
            Assert.That(ally, Is.EqualTo(-1));
            Assert.That(offGuard, Is.True);
        });
    }

    [Test]
    public async Task ANewTauntEndsTheOldOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool first = true, second = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var one = Spawn("CFGuardMagusFoe", 1);
            var two = Spawn("CFGuardMagusFoe", 2);
            Taunts.Taunt(hero, one);
            Taunts.Taunt(hero, two);
            first = Taunts.IsTauntedBy(one, hero);
            second = Taunts.IsTauntedBy(two, hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.True);
        });
    }

    [Test]
    public async Task InterceptAttackTakesPhysicalDamage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int left = 1, guardianHurt = 0, fire = 0, far = 0;
        await Server.WaitPost(() =>
        {
            var reactions = SEntMan.System<CFReactionSystem>();
            var hero = Spawn();
            GiveFeature(hero, "Guardian", "Guardian's Techniques");
            var ally = Spawn(x: 1);
            var foe = Spawn("CFGuardMagusFoe", 1);

            fire = reactions.AnswerDamage(ally, foe, 10, "Fire");
            left = reactions.AnswerDamage(ally, foe, 10, "Slashing");
            guardianHurt = Hurt(hero);

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ReactionSpent = 0;
            far = reactions.AnswerDamage(Spawn(x: FarAway), foe, 10, "Slashing");
        });

        Assert.Multiple(() =>
        {
            Assert.That(fire, Is.EqualTo(10));
            Assert.That(left, Is.EqualTo(0));
            Assert.That(guardianHurt, Is.EqualTo(10));
            Assert.That(far, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task ProudNailNeedsAnIgnoredTaunt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = true, after = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var mate = Spawn(x: 1);
            var foe = Spawn("CFGuardMagusFoe", 1);
            GiveFeat(hero, "CFProudNail");
            var nail = Action(hero, "CFActProudNail");
            Taunts.Taunt(hero, foe);
            before = CanUse(hero, nail, foe);
            Taunts.NoteAttack(foe, mate);
            after = CanUse(hero, nail, foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
        });
    }
}
