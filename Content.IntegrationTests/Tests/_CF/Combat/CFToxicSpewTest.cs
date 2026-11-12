using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.NPC.HTN;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>
/// Toxic Spew (<c>CFActToxicSpew</c>): a breath that pours out as liquid (<see cref="CFBreathFlowSystem"/>). Nothing is
/// granted it yet, so a test dragon carries it.
/// </summary>
[TestFixture]
public sealed class CFToxicSpewTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // Reflex -10, so even a natural 20 only gets a success against DC 21: the spew always deals some damage.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSpewTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 1000
    currentHp: 1000
    printedReflex: -10
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFMobJuvenileHornedDragon
  id: CFSpewTestDragon
  components:
  - type: ActionGrant
    actions:
    - CFActToxicSpew
";

    private CFDevMap _map = default!;

    /// <summary>A spewer that only does what the test tells it: left to think, it walks up and bites the hero.</summary>
    private EntityUid SpawnSpewer(int x, int y)
    {
        var spewer = SEntMan.SpawnAttachedTo("CFSpewTestDragon", _map.Local(x, y));
        SEntMan.RemoveComponent<HTNComponent>(spewer);
        SEntMan.RemoveComponent<CFGOAPComponent>(spewer);
        return spewer;
    }

    private Entity<ActionComponent> Breath(EntityUid spewer)
    {
        Assert.That(SEntMan.System<CFBreathWeaponSystem>().TryGetBreath(spewer, out var breath), "It has a breath weapon.");
        return breath!.Value;
    }

    [Test]
    public async Task OnlyTheSpewIsGranted()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string id = null;
        await Server.WaitPost(() =>
        {
            var breath = Breath(SpawnSpewer(0, 0));
            id = SEntMan.GetComponent<MetaDataComponent>(breath).EntityPrototype?.ID;
        });

        Assert.That(id, Is.EqualTo("CFActToxicSpew"));
    }

    private int CountPuddles()
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<PuddleComponent>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>The breath puddle on the tile a creature stands on, if the liquid has reached it.</summary>
    private EntityUid? PuddleUnder(EntityUid uid)
    {
        var coords = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(uid);
        foreach (var puddle in SEntMan.System<EntityLookupSystem>().GetEntitiesInRange<PuddleComponent>(coords, 0.45f))
        {
            return puddle.Owner;
        }

        return null;
    }

    /// <summary>
    /// Toxic Spew rolls out as green liquid over 2 seconds. Nobody rolls until it has filled; then whoever stands in it
    /// does, and the puddles are left to evaporate.
    /// </summary>
    [Test]
    public async Task ItPoursOutThenHurtsWhatStandsInIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid dragon = default;
        EntityUid hero = default;
        var puddlesBefore = 0;
        var hurtAtOnce = -1;
        var spent = 0;
        var first = false;
        var second = true;
        var recharge = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            puddlesBefore = CountPuddles();
            dragon = SpawnSpewer(0, 0);
            hero = SEntMan.SpawnAttachedTo("CFSpewTestHero", _map.Local(3, 0));
            var breathing = SEntMan.System<CFBreathWeaponSystem>();
            var pool = SEntMan.EnsureComponent<CFActionPoolComponent>(dragon);
            var before = SEntMan.System<CFActionPoolSystem>().ReadyCount(dragon, pool);
            var breath = Breath(dragon);
            var aim = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);

            first = breathing.TryBreathe(dragon, breath, aim);
            second = breathing.TryBreathe(dragon, breath, aim);

            hurtAtOnce = 1000 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            spent = before - SEntMan.System<CFActionPoolSystem>().ReadyCount(dragon, pool);
            if (breath.Comp.Cooldown is { } cooldown)
                recharge = cooldown.End - cooldown.Start;
        });

        await RunSeconds(0.9f);

        var puddlesHalfway = 0;
        var hurtHalfway = -1;
        await Server.WaitPost(() =>
        {
            puddlesHalfway = CountPuddles() - puddlesBefore;
            hurtHalfway = 1000 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
        });

        await RunSeconds(1.6f);

        var puddlesFilled = 0;
        var hurt = 0;
        var breathUnderHero = false;
        var evaporates = false;
        await Server.WaitPost(() =>
        {
            puddlesFilled = CountPuddles() - puddlesBefore;
            hurt = 1000 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            if (PuddleUnder(hero) is not { } puddle)
                return;

            evaporates = SEntMan.HasComponent<EvaporationComponent>(puddle);
            var puddleComp = SEntMan.GetComponent<PuddleComponent>(puddle);
            if (SEntMan.System<SharedSolutionContainerSystem>()
                .ResolveSolution(puddle, puddleComp.SolutionName, ref puddleComp.Solution, out var solution))
            {
                breathUnderHero = solution.GetTotalPrototypeQuantity("CFToxicSpew") > 0;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, "It breathes.");
            Assert.That(spent, Is.EqualTo(2), "Breathing is 2 actions.");
            Assert.That(recharge.TotalSeconds, Is.InRange(6, 24), "1d4 rounds.");
            Assert.That(second, Is.False, "It cannot breathe again until it recharges.");
            Assert.That(hurtAtOnce, Is.Zero, "Nobody rolls the moment it breathes.");
            Assert.That(hurtHalfway, Is.Zero, "Nor while it is still pouring out.");
            Assert.That(puddlesHalfway, Is.InRange(1, puddlesFilled - 1), "It spreads out tile by tile.");
            Assert.That(breathUnderHero, "The liquid reached the hero's tile.");
            Assert.That(evaporates, "The puddles evaporate.");
            Assert.That(hurt, Is.InRange(2, 60), "Once filled: 5d6 poison, halved at best.");
        });
    }

    [Test]
    public async Task SteppingOutBeforeItFillsAvoidsTheBreath()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        var breathed = false;
        await Server.WaitPost(() =>
        {
            var dragon = SpawnSpewer(0, 0);
            hero = SEntMan.SpawnAttachedTo("CFSpewTestHero", _map.Local(3, 0));
            var aim = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
            breathed = SEntMan.System<CFBreathWeaponSystem>().TryBreathe(dragon, Breath(dragon), aim);
        });

        await RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            // Behind the dragon, out of the cone.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, _map.Local(-1, 0));
        });

        await RunSeconds(2f);

        var hurt = -1;
        await Server.WaitPost(() =>
        {
            hurt = 1000 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(breathed);
            Assert.That(hurt, Is.Zero, "They were out of the liquid when it filled.");
        });
    }
}
