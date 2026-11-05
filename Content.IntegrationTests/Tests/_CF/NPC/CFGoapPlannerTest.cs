using System.Collections.Generic;
using System.Numerics;
using Content.Server.CF.NPC;
using Content.Shared.CF.NPC;

namespace Content.IntegrationTests.Tests.CF.NPC;

/// <summary>The planner and node scoring as plain math. These need no game running.</summary>
[TestFixture]
[TestOf(typeof(CFGoapPlanner))]
public sealed class CFGoapPlannerTest
{
    private static CFGoapConditions Has(params CFGoapFact[] facts)
    {
        return CFGoapConditions.From(facts, System.Array.Empty<CFGoapFact>());
    }

    private static CFGoapOption Option(int index, CFGoapConditions needs, CFGoapConditions gives, float cost = 1f)
    {
        return new CFGoapOption(index, needs, gives, cost);
    }

    private const int Strike = 0;
    private const int Close = 1;
    private const int Arm = 2;
    private const int Scent = 3;
    private const int Node = 4;

    private static readonly List<CFGoapOption> Options = new()
    {
        Option(Strike, Has(CFGoapFact.HasWeapon, CFGoapFact.TargetVisible, CFGoapFact.TargetInReach), Has(CFGoapFact.TargetHurt)),
        Option(Close, Has(CFGoapFact.TargetVisible), Has(CFGoapFact.TargetInReach)),
        Option(Arm, Has(CFGoapFact.WeaponNearby), Has(CFGoapFact.HasWeapon), 2f),
        Option(Scent, Has(CFGoapFact.ScentNearby), Has(CFGoapFact.TargetVisible), 2f),
        Option(Node, Has(CFGoapFact.NodeNearby), Has(CFGoapFact.TargetVisible), 3f),
    };

    private static uint World(params CFGoapFact[] facts)
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
        var world = World(CFGoapFact.WeaponNearby, CFGoapFact.TargetVisible);
        var plan = CFGoapPlanner.Plan(world, Has(CFGoapFact.TargetHurt), Options);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.IndexOf(Arm), Is.LessThan(plan.IndexOf(Strike)));
        Assert.That(plan.IndexOf(Close), Is.LessThan(plan.IndexOf(Strike)));
        Assert.That(plan[^1], Is.EqualTo(Strike));
    }

    [Test]
    public void ItPicksTheCheaperRoute()
    {
        var world = World(CFGoapFact.ScentNearby, CFGoapFact.NodeNearby);
        var plan = CFGoapPlanner.Plan(world, Has(CFGoapFact.TargetVisible), Options);

        Assert.That(plan, Is.EqualTo(new[] { Scent }));
    }

    [Test]
    public void ACostChangeSwapsTheRoute()
    {
        var options = new List<CFGoapOption>(Options)
        {
            [Scent] = Options[Scent] with { Cost = 10f },
        };

        var world = World(CFGoapFact.ScentNearby, CFGoapFact.NodeNearby);
        Assert.That(CFGoapPlanner.Plan(world, Has(CFGoapFact.TargetVisible), options), Is.EqualTo(new[] { Node }));
    }

    [Test]
    public void AGoalNothingCanReachHasNoPlan()
    {
        Assert.That(CFGoapPlanner.Plan(0, Has(CFGoapFact.TargetHurt), Options), Is.Null);
    }

    [Test]
    public void AGoalAlreadyMetNeedsNoSteps()
    {
        var plan = CFGoapPlanner.Plan(World(CFGoapFact.TargetHurt), Has(CFGoapFact.TargetHurt), Options);
        Assert.That(plan, Is.Empty);
    }

    [Test]
    public void ItCanClearAFact()
    {
        var healed = CFGoapConditions.From(System.Array.Empty<CFGoapFact>(), new[] { CFGoapFact.Hurt });
        var options = new List<CFGoapOption> { Option(0, Has(CFGoapFact.HasHealingItem), healed) };

        var plan = CFGoapPlanner.Plan(World(CFGoapFact.Hurt, CFGoapFact.HasHealingItem), healed, options);
        Assert.That(plan, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void NodesAheadOfTheTargetScoreHigher()
    {
        var heading = Vector2.UnitX;
        var ahead = CFNodeScore.Score(1f, new Vector2(3f, 0f), heading, false, float.MaxValue);
        var behind = CFNodeScore.Score(1f, new Vector2(-3f, 0f), heading, false, float.MaxValue);

        Assert.That(ahead, Is.GreaterThan(behind));
        Assert.That(behind, Is.GreaterThan(0f), "A node behind it is less likely, not ruled out.");
    }

    [Test]
    public void ScentCloserAndUncheckedNodesScoreHigher()
    {
        var offset = new Vector2(3f, 0f);
        var plain = CFNodeScore.Score(1f, offset, Vector2.Zero, false, float.MaxValue);

        Assert.That(CFNodeScore.Score(1f, offset, Vector2.Zero, true, float.MaxValue), Is.GreaterThan(plain));
        Assert.That(CFNodeScore.Score(1f, offset * 3f, Vector2.Zero, false, float.MaxValue), Is.LessThan(plain));
        Assert.That(CFNodeScore.Score(1f, offset, Vector2.Zero, false, 0f), Is.LessThan(plain));
        Assert.That(CFNodeScore.Score(1f, offset, Vector2.Zero, false, float.MaxValue, 3f), Is.GreaterThan(plain));
    }

    /// <summary>A 12x7 room with a wall down the middle and one gap at the top.</summary>
    private static int RoomCost(Robust.Shared.Maths.Vector2i tile)
    {
        return RoomWithWall(tile) ? 0 : CFFlowFieldBuilder.Blocked;
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
        var arrows = CFFlowFieldBuilder.Build(goal, RoomCost, 40);

        // Anywhere at all, which is why nobody can fall behind.
        foreach (var start in new Robust.Shared.Maths.Vector2i[] { new(0, 0), new(5, 3), new(0, 6), new(6, 6), new(11, 6) })
        {
            Assert.That(Walk(arrows, start), Is.EqualTo(goal), $"From {start}.");
        }
    }

    [Test]
    public void ArrowsDoNotCutCorners()
    {
        var arrows = CFFlowFieldBuilder.Build(new Robust.Shared.Maths.Vector2i(10, 0), RoomCost, 40);
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
        var arrows = CFFlowFieldBuilder.Build(new Robust.Shared.Maths.Vector2i(10, 0), t => RoomWithSlowTile(t, 10), 40);
        var arrow = arrows[new Robust.Shared.Maths.Vector2i(5, 0)];
        Assert.That(arrow.X, Is.GreaterThan(0f), "It goes through.");
    }

    [Test]
    public void TheLongWayRoundBeatsADearWindow()
    {
        var arrows = CFFlowFieldBuilder.Build(new Robust.Shared.Maths.Vector2i(10, 0), t => RoomWithSlowTile(t, 200), 40);
        var arrow = arrows[new Robust.Shared.Maths.Vector2i(5, 0)];
        Assert.That(arrow.X, Is.LessThanOrEqualTo(0.001f), "Going around costs less than breaking through.");
    }

    [Test]
    public void AFieldStopsAtItsReach()
    {
        var arrows = CFFlowFieldBuilder.Build(Robust.Shared.Maths.Vector2i.Zero, _ => 0, 5);
        Assert.That(arrows.ContainsKey(new Robust.Shared.Maths.Vector2i(4, 0)));
        Assert.That(arrows.ContainsKey(new Robust.Shared.Maths.Vector2i(20, 0)), Is.False, "An open map must not cost more than a closed one.");
    }
}
