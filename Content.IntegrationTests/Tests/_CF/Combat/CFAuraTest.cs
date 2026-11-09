using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>The Champion's aura, on the real feats.</summary>
[TestFixture]
[TestOf(typeof(CFAuraSystem))]
public sealed class CFAuraTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);
    private const int FarAway = 7;
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAuraHero
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
  parent: CFAuraHero
  id: CFAuraFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private static readonly HashSet<ProtoId<CFTraitPrototype>> Disease = new() { "Disease" };
    private static readonly HashSet<ProtoId<CFTraitPrototype>> Fear = new() { "Fear" };

    private CFDevMap _map = default!;

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Spawn(string id = "CFAuraHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Champion(params string[] feats)
    {
        var hero = Spawn();
        var features = SEntMan.System<CFCharacterFeatureSystem>();
        var aura = Server.ProtoMan.Index<CFClassPrototype>("Champion").Features.First(f => f.Name == "Champion's Aura");
        features.Apply(hero, aura.Grants, "feature:aura");
        foreach (var id in feats)
        {
            features.Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>(id).Grants, $"feat:{id}");
        }

        return hero;
    }

    private void Move(EntityUid mob, int x)
    {
        SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, _map.Local(x, 0));
    }

    [Test]
    public async Task ABonusLastsWhileInsideAndOnlyForAllies()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid ally = default, foe = default, hero = default;
        int inside = 0, enemy = 0, own = 0, outside = 0;
        await Server.WaitPost(() =>
        {
            hero = Champion("CFDivineHealth");
            ally = Spawn(x: 1);
            foe = Spawn("CFAuraFoe", 1);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            inside = Bonuses.Total(ally, CFBonusStat.Saves, traits: Disease);
            enemy = Bonuses.Total(foe, CFBonusStat.Saves, traits: Disease);
            own = Bonuses.Total(hero, CFBonusStat.Saves, traits: Disease);
            Move(ally, FarAway);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => outside = Bonuses.Total(ally, CFBonusStat.Saves, traits: Disease));

        Assert.Multiple(() =>
        {
            Assert.That(inside, Is.EqualTo(1));
            Assert.That(enemy, Is.EqualTo(0));
            Assert.That(own, Is.EqualTo(2), "The aura does not add to the champion's own bonus.");
            Assert.That(outside, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ABonusEndsWhenTheAuraIsRemoved()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid ally = default, hero = default;
        int before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            hero = Champion("CFDivineHealth");
            ally = Spawn(x: 1);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            before = Bonuses.Total(ally, CFBonusStat.Saves, traits: Disease);
            SEntMan.RemoveComponent<CFAuraComponent>(hero);
        });
        await Pair.RunTicksSync(2);
        await Server.WaitPost(() => after = Bonuses.Total(ally, CFBonusStat.Saves, traits: Disease));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(after, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AuraOfCourageEasesAlliesAndLightensTheChampion()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid ally = default;
        int champion = 0, eased = 0;
        await Server.WaitPost(() =>
        {
            var hero = Champion("CFAuraofCourage");
            Conditions.ApplyFrightened(hero, 2);
            champion = Conditions.Frightened(hero);
            ally = Spawn(x: 1);
            SEntMan.GetComponent<CFActionPoolComponent>(ally).MaxActions = 30;
            Conditions.ApplyFrightened(ally, 3);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => eased = Conditions.Frightened(ally));

        Assert.Multiple(() =>
        {
            Assert.That(champion, Is.EqualTo(1));
            Assert.That(eased, Is.EqualTo(2), "The first check after the aura appears eases by 1.");
        });
    }

    [Test]
    public async Task AuraOfDespairHoldsFrightenedAtOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid foe = default, other = default;
        int inside = 0, outside = 0, penalty = 0;
        await Server.WaitPost(() =>
        {
            Champion("CFAuraofDespair");
            foe = Spawn("CFAuraFoe", 1);
            other = Spawn("CFAuraFoe", FarAway);
            Conditions.ApplyFrightened(foe, 1);
            Conditions.ApplyFrightened(other, 1);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            penalty = Bonuses.Total(foe, CFBonusStat.Saves, traits: Fear);
            SEntMan.EventBus.RaiseLocalEvent(foe, new CFTurnRefilledEvent());
            SEntMan.EventBus.RaiseLocalEvent(other, new CFTurnRefilledEvent());
            inside = Conditions.Frightened(foe);
            outside = Conditions.Frightened(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(penalty, Is.EqualTo(-1));
            Assert.That(inside, Is.EqualTo(1));
            Assert.That(outside, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ShieldsOfTheSpiritProtectAlliesWhileTheShieldIsUp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, ally = default, foe = default;
        var raisedWithoutShield = true;
        int armor = 0, hurt = 0, afterLowered = 0;
        await Server.WaitPost(() =>
        {
            hero = Champion();
            SEntMan.System<CFCharacterFeatureSystem>().Apply(
                hero,
                Server.ProtoMan.Index<CFClassChoicePrototype>("DevotionShieldsOfTheSpirit").Grants,
                "choice:shields");
            ally = Spawn(x: 1);
            foe = Spawn("CFAuraFoe", 2);

            var auras = SEntMan.System<CFAuraSystem>();
            raisedWithoutShield = auras.RaiseSpiritShields(hero);
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, Spawn("CFSteelShield"));
            var actions = SEntMan.System<SharedActionsSystem>();
            var cast = actions.GetActions(hero)
                .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "CFShieldsOfTheSpirit");
            actions.PerformAction(hero, cast, new CFSpellCastInstantEvent());
            armor = Bonuses.Total(ally, CFBonusStat.ArmorClass);

            auras.NoteAttacked(ally, foe);
            hurt = StartHp - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;

            var shields = SEntMan.System<CFShieldSystem>();
            shields.TryGetRaisedShield(hero, out var shield);
            shields.SetRaised(shield, false);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => afterLowered = Bonuses.Total(ally, CFBonusStat.ArmorClass));

        Assert.Multiple(() =>
        {
            Assert.That(raisedWithoutShield, Is.False);
            Assert.That(armor, Is.EqualTo(1));
            Assert.That(hurt, Is.InRange(1, 4));
            Assert.That(afterLowered, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ShieldsOfTheSpiritNeedAShieldInHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var cancelled = false;
        await Server.WaitPost(() =>
        {
            var hero = Champion();
            var action = SEntMan.SpawnEntity("CFShieldsOfTheSpirit", Robust.Shared.Map.MapCoordinates.Nullspace);
            var attempt = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
            cancelled = attempt.Cancelled;
        });

        Assert.That(cancelled, Is.True);
    }
}
