using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Effects;

/// <summary>The counteract rule, and Dispel Magic ending real spells.</summary>
[TestFixture]
[TestOf(typeof(CFCounteractSystem))]
public sealed class CFCounteractTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCounteractTestCaster
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private CFCounteractSystem Counteract => SEntMan.System<CFCounteractSystem>();

    private EntityUid Mob() => SEntMan.SpawnEntity("CFCounteractTestCaster", _map.Local(0, 0));

    private EntityUid Spell(string id) => SEntMan.SpawnEntity(id, MapCoordinates.Nullspace);

    [TestCase(CFDegree.CriticalSuccess, 1, 4, true)]
    [TestCase(CFDegree.CriticalSuccess, 1, 5, false)]
    [TestCase(CFDegree.Success, 2, 3, true)]
    [TestCase(CFDegree.Success, 2, 4, false)]
    [TestCase(CFDegree.Failure, 2, 1, true)]
    [TestCase(CFDegree.Failure, 2, 2, false)]
    [TestCase(CFDegree.CriticalFailure, 5, 1, false)]
    public void TheDegreeDecidesHowHighItReaches(CFDegree degree, int counteract, int target, bool ends)
    {
        Assert.That(CFCounteractSystem.Counteracts(degree, counteract, target), Is.EqualTo(ends));
    }

    [Test]
    public async Task DispellingBlessTakesItsBonusesAway()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, after = 0;
        var found = false;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var target = Mob();
            SEntMan.System<CFEffectSystem>().ResolveTarget(Spell("CFBless"), Mob(), target);
            before = bonuses.Total(target, CFBonusStat.AttackRoll);

            found = Counteract.TryDispel(Mob(), target, 2, 0, d20: 20);
            after = bonuses.Total(target, CFBonusStat.AttackRoll);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(found, Is.True);
            Assert.That(after, Is.Zero);
        });
    }

    [Test]
    public async Task AFailedCounteractLeavesTheConditionAndAGoodOneEndsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int held = 0, ended = -1;
        var nothing = true;
        await Server.WaitPost(() =>
        {
            var caster = Mob();
            var target = Mob();
            var args = new CFEffectArgs { User = caster, Source = Spell("CFFear"), Target = target, Rank = 3, Dc = 30 };
            Counteract.Note(args, target, 60f, CFConditionKind.Frightened);
            SEntMan.System<CFConditionSystem>().ApplyCondition(target, CFConditionKind.Frightened, 2, 60f, caster, 30);
            var cond = SEntMan.GetComponent<CFConditionComponent>(target);

            Counteract.TryDispel(Mob(), target, 1, 0, d20: 1);
            held = cond.Frightened;

            Counteract.TryDispel(Mob(), target, 1, 25, d20: 15);
            ended = cond.Frightened;

            nothing = !Counteract.TryDispel(Mob(), Mob(), 1, 0, d20: 20);
        });

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EqualTo(2), "A critical failure ends nothing.");
            Assert.That(ended, Is.Zero, "A critical success reaches three ranks higher.");
            Assert.That(nothing, Is.True, "A creature with no spell on it has nothing to aim at.");
        });
    }
}
