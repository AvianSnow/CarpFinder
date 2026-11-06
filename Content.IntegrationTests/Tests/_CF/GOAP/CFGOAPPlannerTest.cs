using System.Collections.Generic;
using System.Numerics;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Nodes;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>The planner and node scoring as plain math. These need no game running.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPPlanner))]
public sealed class CFGOAPPlannerTest
{
    private static CFGOAPConditions Has(params CFGOAPFact[] facts)
    {
        return CFGOAPConditions.From(facts, System.Array.Empty<CFGOAPFact>());
    }

    private static CFGOAPOption Option(int index, CFGOAPConditions needs, CFGOAPConditions gives, float cost = 1f)
    {
        return new CFGOAPOption(index, needs, gives, cost);
    }

    private const int Strike = 0;
    private const int Close = 1;
    private const int Arm = 2;
    private const int Scent = 3;
    private const int Node = 4;

    private static readonly List<CFGOAPOption> Options = new()
    {
        Option(Strike, Has(CFGOAPFact.HasWeapon, CFGOAPFact.TargetVisible, CFGOAPFact.TargetInReach), Has(CFGOAPFact.TargetHurt)),
        Option(Close, Has(CFGOAPFact.TargetVisible), Has(CFGOAPFact.TargetInReach)),
        Option(Arm, Has(CFGOAPFact.WeaponNearby), Has(CFGOAPFact.HasWeapon), 2f),
        Option(Scent, Has(CFGOAPFact.ScentNearby), Has(CFGOAPFact.TargetVisible), 2f),
        Option(Node, Has(CFGOAPFact.NodeNearby), Has(CFGOAPFact.TargetVisible), 3f),
    };

    private static uint World(params CFGOAPFact[] facts)
    {
        uint world = 0;
        foreach (var fact in facts)
        {
            world |= (uint) fact;
        }

        return world;
    }

    [Test]
    public void AnUnarmedCreatureArmsItselfBeforeAttacking()
    {
        var world = World(CFGOAPFact.WeaponNearby, CFGOAPFact.TargetVisible);
        var plan = CFGOAPPlanner.Plan(world, Has(CFGOAPFact.TargetHurt), Options);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.IndexOf(Arm), Is.LessThan(plan.IndexOf(Strike)));
        Assert.That(plan.IndexOf(Close), Is.LessThan(plan.IndexOf(Strike)));
        Assert.That(plan[^1], Is.EqualTo(Strike));
    }

    [Test]
    public void ItPicksTheCheaperRoute()
    {
        var world = World(CFGOAPFact.ScentNearby, CFGOAPFact.NodeNearby);
        var plan = CFGOAPPlanner.Plan(world, Has(CFGOAPFact.TargetVisible), Options);

        Assert.That(plan, Is.EqualTo(new[] { Scent }));
    }

    [Test]
    public void ACostChangeSwapsTheRoute()
    {
        var options = new List<CFGOAPOption>(Options)
        {
            [Scent] = Options[Scent] with { Cost = 10f },
        };

        var world = World(CFGOAPFact.ScentNearby, CFGOAPFact.NodeNearby);
        Assert.That(CFGOAPPlanner.Plan(world, Has(CFGOAPFact.TargetVisible), options), Is.EqualTo(new[] { Node }));
    }

    /// <summary>Costs go below 1, so counting each missing fact as a flat 1 could settle for the dearer plan.</summary>
    [Test]
    public void TwoCheapStepsBeatOneDearOne()
    {
        const int dear = 0;
        const int flank = 1;
        const int strike = 2;
        var options = new List<CFGOAPOption>
        {
            Option(dear, Has(CFGOAPFact.TargetVisible), Has(CFGOAPFact.TargetHurt), 1.3f),
            Option(flank, Has(CFGOAPFact.TargetVisible), Has(CFGOAPFact.AtFlank), 0.4f),
            Option(strike, Has(CFGOAPFact.AtFlank), Has(CFGOAPFact.TargetHurt), 0.4f),
        };

        var plan = CFGOAPPlanner.Plan(World(CFGOAPFact.TargetVisible), Has(CFGOAPFact.TargetHurt), options);
        Assert.That(plan, Is.EqualTo(new[] { flank, strike }));
    }

    [Test]
    public void AGoalNothingCanReachHasNoPlan()
    {
        Assert.That(CFGOAPPlanner.Plan(0, Has(CFGOAPFact.TargetHurt), Options), Is.Null);
    }

    [Test]
    public void AGoalAlreadyMetNeedsNoSteps()
    {
        var plan = CFGOAPPlanner.Plan(World(CFGOAPFact.TargetHurt), Has(CFGOAPFact.TargetHurt), Options);
        Assert.That(plan, Is.Empty);
    }

    [Test]
    public void ItCanClearAFact()
    {
        var healed = CFGOAPConditions.From(System.Array.Empty<CFGOAPFact>(), new[] { CFGOAPFact.Hurt });
        var options = new List<CFGOAPOption> { Option(0, Has(CFGOAPFact.HasHealingItem), healed) };

        var plan = CFGOAPPlanner.Plan(World(CFGOAPFact.Hurt, CFGOAPFact.HasHealingItem), healed, options);
        Assert.That(plan, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void NodesAheadOfTheTargetScoreHigher()
    {
        var heading = Vector2.UnitX;
        var ahead = CFGOAPNodeScore.Score(1f, new Vector2(3f, 0f), heading, false, float.MaxValue);
        var behind = CFGOAPNodeScore.Score(1f, new Vector2(-3f, 0f), heading, false, float.MaxValue);

        Assert.That(ahead, Is.GreaterThan(behind));
        Assert.That(behind, Is.GreaterThan(0f), "A node behind it is less likely, not ruled out.");
    }

    [Test]
    public void ScentCloserAndUncheckedNodesScoreHigher()
    {
        var offset = new Vector2(3f, 0f);
        var plain = CFGOAPNodeScore.Score(1f, offset, Vector2.Zero, false, float.MaxValue);

        Assert.That(CFGOAPNodeScore.Score(1f, offset, Vector2.Zero, true, float.MaxValue), Is.GreaterThan(plain));
        Assert.That(CFGOAPNodeScore.Score(1f, offset * 3f, Vector2.Zero, false, float.MaxValue), Is.LessThan(plain));
        Assert.That(CFGOAPNodeScore.Score(1f, offset, Vector2.Zero, false, 0f), Is.LessThan(plain));
        Assert.That(CFGOAPNodeScore.Score(1f, offset, Vector2.Zero, false, float.MaxValue, 3f), Is.GreaterThan(plain));
    }

    /// <summary>A 12x7 room with a wall down the middle and one gap at the top.</summary>
    private static int RoomCost(Robust.Shared.Maths.Vector2i tile)
    {
        return RoomWithWall(tile) ? 0 : CFGOAPFlowFieldBuilder.Blocked;
    }

    private static bool RoomWithWall(Robust.Shared.Maths.Vector2i tile)
    {
        if (tile.X < 0 || tile.X > 11 || tile.Y < 0 || tile.Y > 6)
            return false;

        return tile.X != 6 || tile.Y == 6;
    }

    /// <summary>Walks the arrows from a tile, and returns where it ends up.</summary>
    private static Robust.Shared.Maths.Vector2i Walk(Dictionary<Robust.Shared.Maths.Vector2i, Vector2> arrows, Robust.Shared.Maths.Vector2i tile)
    {
        for (var step = 0; step < 100 && arrows.TryGetValue(tile, out var arrow); step++)
        {
            tile += new Robust.Shared.Maths.Vector2i(System.Math.Sign(MathF.Round(arrow.X)), System.Math.Sign(MathF.Round(arrow.Y)));
            Assert.That(RoomWithWall(tile), $"An arrow led into a wall at {tile}.");
        }

        return tile;
    }

    [Test]
    public void EveryTileLeadsToTheGoalAroundTheWall()
    {
        var goal = new Robust.Shared.Maths.Vector2i(10, 0);
        var arrows = CFGOAPFlowFieldBuilder.Build(goal, RoomCost, 40);

        // Anywhere at all, which is why nobody can fall behind.
        foreach (var start in new Robust.Shared.Maths.Vector2i[] { new(0, 0), new(5, 3), new(0, 6), new(6, 6), new(11, 6) })
        {
            Assert.That(Walk(arrows, start), Is.EqualTo(goal), $"From {start}.");
        }
    }

    [Test]
    public void ArrowsDoNotCutCorners()
    {
        var arrows = CFGOAPFlowFieldBuilder.Build(new Robust.Shared.Maths.Vector2i(10, 0), RoomCost, 40);
        var arrow = arrows[new Robust.Shared.Maths.Vector2i(5, 5)];

        Assert.That(arrow.X, Is.EqualTo(0f).Within(0.001f), "Going diagonally here would clip the end of the wall.");
        Assert.That(arrow.Y, Is.GreaterThan(0f));
    }

    /// <summary>A wall along x = 6 with a slow tile in it at y = 0, and the end of the wall at y = 6.</summary>
    private static int RoomWithSlowTile(Robust.Shared.Maths.Vector2i tile, int slowCost)
    {
        return tile == new Robust.Shared.Maths.Vector2i(6, 0) ? slowCost : RoomCost(tile);
    }

    [Test]
    public void ACheapDoorBeatsTheLongWayRound()
    {
        var arrows = CFGOAPFlowFieldBuilder.Build(new Robust.Shared.Maths.Vector2i(10, 0), t => RoomWithSlowTile(t, 10), 40);
        var arrow = arrows[new Robust.Shared.Maths.Vector2i(5, 0)];
        Assert.That(arrow.X, Is.GreaterThan(0f), "It goes through.");
    }

    [Test]
    public void TheLongWayRoundBeatsADearWindow()
    {
        var arrows = CFGOAPFlowFieldBuilder.Build(new Robust.Shared.Maths.Vector2i(10, 0), t => RoomWithSlowTile(t, 200), 40);
        var arrow = arrows[new Robust.Shared.Maths.Vector2i(5, 0)];
        Assert.That(arrow.X, Is.LessThanOrEqualTo(0.001f), "Going around costs less than breaking through.");
    }

    /// <summary>The nearest wall is close, and the real space is through the gap.</summary>
    [Test]
    public void ARunnerGoesThroughTheGapAndNotIntoTheNearestCorner()
    {
        var arrows = CFGOAPFlowFieldBuilder.BuildFlee(new Robust.Shared.Maths.Vector2i(2, 3), RoomCost, 40);

        var end = Walk(arrows, new Robust.Shared.Maths.Vector2i(4, 3));
        Assert.That(end.X, Is.GreaterThan(6), "It ends up on the far side of the wall.");
    }

    [Test]
    public void ADeadEndIsAsSafeAsItGets()
    {
        static int Corridor(Robust.Shared.Maths.Vector2i tile)
        {
            return tile.Y == 0 && tile.X >= 0 && tile.X <= 5 ? 0 : CFGOAPFlowFieldBuilder.Blocked;
        }

        var arrows = CFGOAPFlowFieldBuilder.BuildFlee(new Robust.Shared.Maths.Vector2i(5, 0), Corridor, 40);

        Assert.That(arrows[new Robust.Shared.Maths.Vector2i(3, 0)].X, Is.LessThan(0f), "It backs away.");
        Assert.That(arrows[Robust.Shared.Maths.Vector2i.Zero], Is.EqualTo(Vector2.Zero), "There is nowhere further to go.");
    }

    [Test]
    public void AFieldStopsAtItsReach()
    {
        var arrows = CFGOAPFlowFieldBuilder.Build(Robust.Shared.Maths.Vector2i.Zero, _ => 0, 5);
        Assert.That(arrows.ContainsKey(new Robust.Shared.Maths.Vector2i(4, 0)));
        Assert.That(arrows.ContainsKey(new Robust.Shared.Maths.Vector2i(20, 0)), Is.False, "An open map must not cost more than a closed one.");
    }
}
