using Content.IntegrationTests.Fixtures;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.ZLevels;
using Content.Shared.Chasm;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.ZLevels;

/// <summary>
/// A creature at a chasm: touching the edge is safe, a slip floats it weightless so it can get back, and after the
/// grace it rolls Grab an Edge. Dexterity is extreme so a natural 1 or 20 cannot change the outcome.
/// </summary>
[TestFixture]
[TestOf(typeof(CFChasmEdgeSystem))]
public sealed class CFChasmEdgeTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFChasmTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
";

    private CFDevMap _map = default!;

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private Entity<CFChasmEdgeComponent> Chasm()
    {
        var chasm = SEntMan.SpawnEntity("FloorChasmEntity", _map.Local(0, 0));
        var edge = SEntMan.GetComponent<CFChasmEdgeComponent>(chasm);
        edge.Grace = TimeSpan.FromSeconds(1);
        return (chasm, edge);
    }

    /// <summary>
    /// The map and the chasm, then a few ticks so the chasm is anchored and settled before any creature arrives.
    /// </summary>
    private async Task<Entity<CFChasmEdgeComponent>> Setup()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        Entity<CFChasmEdgeComponent> chasm = default;
        await Server.WaitPost(() => chasm = Chasm());
        await Pair.RunTicksSync(5);
        return chasm;
    }

    private EntityUid Human(float x, float y, int dexterity)
    {
        var local = _map.Local(0, 0);
        var mob = SEntMan.SpawnEntity("CFChasmTestHuman", local.Offset(new System.Numerics.Vector2(x, y)));
        SEntMan.GetComponent<CFAbilityScoreComponent>(mob).AbilityModifiers[CFAbility.Dexterity] = dexterity;
        // With no class, a save is the Commoner's unless it is printed, as a monster's is.
        SEntMan.GetComponent<CFCombatantComponent>(mob).PrintedReflex = dexterity;
        SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
        return mob;
    }

    private bool Fell(EntityUid mob)
    {
        return !SEntMan.EntityExists(mob) || SEntMan.HasComponent<ChasmFallingComponent>(mob);
    }

    private bool Slipping(EntityUid mob)
    {
        return SEntMan.TryGetComponent(mob, out CFChasmSlipComponent slip) && slip.Slipping;
    }

    private Task Seconds(float seconds) => Pair.RunTicksSync((int) (Timing.TickRate * seconds));

    [Test]
    public async Task TouchingTheEdgeIsSafe()
    {
        await Setup();
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            // Its centre is on the floor beside the chasm, and a quarter tile of its body hangs over.
            mob = Human(0.6f, 0f, dexterity: -30);
        });

        await Seconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Fell(mob), Is.False, "Stock dropped anything whose body touched the chasm at all.");
            Assert.That(Slipping(mob), Is.False);
            Assert.That(SEntMan.System<SharedGravitySystem>().IsWeightless(mob), Is.False);
        });
    }

    [Test]
    public async Task ASlipFloatsAndCanBeWalkedBackFrom()
    {
        var chasm = await Setup();
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            chasm.Comp.Grace = TimeSpan.FromSeconds(10);
            mob = Human(0.6f, 0f, dexterity: -30);
        });

        await Seconds(0.5f);
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, _map.Local(0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
        });
        await Seconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Fell(mob), Is.False, "It has time to get back first.");
            Assert.That(Slipping(mob), "Its centre is over the hole.");
            Assert.That(SEntMan.System<SharedGravitySystem>().IsWeightless(mob), "It floats while slipping.");
        });

        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, _map.Local(2, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
        });
        await Seconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Fell(mob), Is.False);
            Assert.That(SEntMan.HasComponent<CFChasmSlipComponent>(mob), Is.False, "Clear of the chasm, it is let go.");
            Assert.That(SEntMan.System<SharedGravitySystem>().IsWeightless(mob), Is.False);
        });
    }

    [Test]
    public async Task GrabAnEdgePullsItOntoSolidGround()
    {
        await Setup();
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            mob = Human(0f, 0f, dexterity: 30);
        });

        await Seconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Fell(mob), Is.False);
            Assert.That(Slipping(mob), Is.False);

            var xform = SEntMan.GetComponent<TransformComponent>(mob);
            var maps = SEntMan.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);
            var tile = maps.CoordinatesToTile(_map.Grid, grid, xform.Coordinates);
            Assert.That(tile, Is.Not.EqualTo(_map.Origin), "It was pulled off the chasm.");
            Assert.That((tile - _map.Origin).Length, Is.LessThanOrEqualTo(2f), "Within 10 feet.");
        });
    }

    [Test]
    public async Task FailingToGrabFalls()
    {
        await Setup();
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            mob = Human(0f, 0f, dexterity: -30);
        });

        await Seconds(0.5f);
        await Server.WaitAssertion(() => Assert.That(Fell(mob), Is.False, "Not before the grace is over."));
        await Seconds(2f);
        await Server.WaitAssertion(() => Assert.That(Fell(mob), "A failed Reflex save falls as stock does. " + State(mob)));
    }

    private string State(EntityUid mob)
    {
        if (!SEntMan.EntityExists(mob))
            return "Deleted.";

        var slip = SEntMan.GetComponentOrNull<CFChasmSlipComponent>(mob);
        var xform = SEntMan.GetComponent<TransformComponent>(mob);
        var options = SEntMan.GetComponentOrNull<CFCombatOptionsComponent>(mob);
        return $"Slip: {(slip == null ? "none" : $"slipping {slip.Slipping}, ends {slip.SlipEnds}")}, "
               + $"now {Timing.CurTime}, at {xform.Coordinates}, origin {_map.Origin}, "
               + $"weightless {SEntMan.System<SharedGravitySystem>().IsWeightless(mob)}, "
               + $"reaction spent {options?.ReactionSpent}.";
    }
}
