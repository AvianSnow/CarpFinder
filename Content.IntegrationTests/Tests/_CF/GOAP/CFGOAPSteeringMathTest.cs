using System.Numerics;
using Content.Server.CF.GOAP.Influence;
using Content.Server.CF.GOAP.Steering;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>The steering behaviours as plain math. These need no game running.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPSteeringMath))]
public sealed class CFGOAPSteeringMathTest
{
    /// <summary>Ticks until a chaser catches a target running past it at a constant velocity.</summary>
    private static int Chase(bool predict)
    {
        const float speed = 1f;
        const float step = 0.1f;
        var chaser = Vector2.Zero;
        var target = new Vector2(10f, -5f);
        var velocity = new Vector2(0f, 0.8f);
        for (var tick = 0; tick < 1000; tick++)
        {
            var heading = predict
                ? CFGOAPSteeringMath.Pursuit(chaser, speed, target, velocity)
                : CFGOAPSteeringMath.Seek(chaser, target);

            chaser += heading * speed * step;
            target += velocity * step;
            if (Vector2.Distance(chaser, target) < 0.2f)
                return tick;
        }

        return int.MaxValue;
    }

    [Test]
    public void PursuitInterceptsFasterThanSeek()
    {
        Assert.That(Chase(predict: true), Is.LessThan(Chase(predict: false)));
    }

    [Test]
    public void FleeAndEvadeHeadAway()
    {
        var threat = new Vector2(1f, 0f);
        Assert.That(CFGOAPSteeringMath.Flee(Vector2.Zero, threat), Is.EqualTo(new Vector2(-1f, 0f)));
        Assert.That(CFGOAPSteeringMath.Evade(Vector2.Zero, 1f, threat, Vector2.Zero), Is.EqualTo(new Vector2(-1f, 0f)));
        Assert.That(CFGOAPSteeringMath.Seek(Vector2.Zero, Vector2.Zero), Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void ArriveSlowsInsideItsRadius()
    {
        var target = new Vector2(10f, 0f);
        Assert.That(CFGOAPSteeringMath.ArriveSpeed(Vector2.Zero, target, 2f), Is.EqualTo(1f));
        Assert.That(CFGOAPSteeringMath.ArriveSpeed(new Vector2(9f, 0f), target, 2f), Is.EqualTo(0.5f));
        Assert.That(CFGOAPSteeringMath.ArriveSpeed(target, target, 2f), Is.Zero);
    }

    [Test]
    public void ApproachSlowsThenStopsAndWaitsToResume()
    {
        var goal = new Vector2(10f, 0f);
        var settled = false;
        Assert.That(CFGOAPSteeringMath.ApproachSpeed(Vector2.Zero, goal, Vector2.Zero, 4f, 2f, 0f, 0.5f, ref settled), Is.EqualTo(1f), "Far off it goes at full speed.");
        Assert.That(CFGOAPSteeringMath.ApproachSpeed(new Vector2(9f, 0f), goal, Vector2.Zero, 4f, 2f, 0f, 0.5f, ref settled), Is.EqualTo(0.5f), "Halfway into the radius, half speed.");
        Assert.That(CFGOAPSteeringMath.ApproachSpeed(new Vector2(9.8f, 0f), goal, Vector2.Zero, 4f, 2f, 0f, 0.5f, ref settled), Is.EqualTo(CFGOAPSteeringMath.MinApproachSpeed), "It never crawls.");
        Assert.That(settled, Is.False);

        Assert.That(CFGOAPSteeringMath.ApproachSpeed(new Vector2(9.95f, 0f), goal, Vector2.Zero, 4f, 2f, 0f, 0.5f, ref settled), Is.Zero);
        Assert.That(settled, "Close enough, it stops.");
        Assert.That(CFGOAPSteeringMath.ApproachSpeed(new Vector2(9.7f, 0f), goal, Vector2.Zero, 4f, 2f, 0f, 0.5f, ref settled), Is.Zero, "A small shuffle does not set it off again.");
        Assert.That(CFGOAPSteeringMath.ApproachSpeed(new Vector2(9f, 0f), goal, Vector2.Zero, 4f, 2f, 0f, 0.5f, ref settled), Is.GreaterThan(0f), "A real move does.");
        Assert.That(settled, Is.False);
    }

    [Test]
    public void ApproachStopsShortAndKeepsUpWithAGoalWalkingOff()
    {
        var goal = new Vector2(10f, 0f);
        var settled = false;
        Assert.That(CFGOAPSteeringMath.ApproachSpeed(new Vector2(9.1f, 0f), goal, Vector2.Zero, 4f, 2f, 0.85f, 0.25f, ref settled), Is.Zero, "It stops at the standoff, out of their body.");

        settled = false;
        var standing = CFGOAPSteeringMath.ApproachSpeed(new Vector2(8.5f, 0f), goal, Vector2.Zero, 4f, 2f, 0.85f, 0.25f, ref settled);
        var walkingOff = CFGOAPSteeringMath.ApproachSpeed(new Vector2(8.5f, 0f), goal, new Vector2(3f, 0f), 4f, 2f, 0.85f, 0.25f, ref settled);
        var walkingIn = CFGOAPSteeringMath.ApproachSpeed(new Vector2(8.5f, 0f), goal, new Vector2(-3f, 0f), 4f, 2f, 0.85f, 0.25f, ref settled);
        Assert.That(walkingOff, Is.GreaterThan(standing), "A goal walking away is kept up with.");
        Assert.That(walkingIn, Is.EqualTo(standing), "One walking in does not make it brake harder than standing would.");
    }

    [Test]
    public void AimFindsTheWayBetweenTwoSlots()
    {
        // What stock writes for a behaviour heading 15 degrees, halfway between slots 0 and 1.
        var interest = new float[12];
        var danger = new float[12];
        for (var i = 0; i < 12; i++)
        {
            interest[i] = MathF.Max(0f, MathF.Cos(i * MathF.Tau / 12f - MathF.PI / 12f));
        }

        Assert.That(CFGOAPSteeringMath.Aim(interest, danger, 0), Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(CFGOAPSteeringMath.Aim(interest, danger, 1), Is.EqualTo(0.5f).Within(0.01f), "Either best slot gives the same answer.");

        for (var i = 0; i < 12; i++)
        {
            interest[i] = MathF.Max(0f, MathF.Cos(i * MathF.Tau / 12f));
        }

        Assert.That(CFGOAPSteeringMath.Aim(interest, danger, 0), Is.EqualTo(0f).Within(0.001f), "Straight along a slot stays on it.");

        interest[11] = 1f;
        interest[0] = 1f;
        interest[1] = 1f;
        Assert.That(CFGOAPSteeringMath.Aim(interest, danger, 0), Is.EqualTo(0f), "A flat top keeps the best slot.");
    }

    [Test]
    public void WanderStaysRoughlyAhead()
    {
        for (var angle = 0f; angle < MathF.Tau; angle += 0.5f)
        {
            Assert.That(Vector2.Dot(CFGOAPSteeringMath.Wander(Vector2.UnitX, angle, 1f), Vector2.UnitX), Is.GreaterThan(0f));
        }
    }

    [Test]
    public void ANewerMarkBeatsAStrongerOne()
    {
        var old = new CFGOAPInfluenceCell { Intensity = 1f, LastVisit = TimeSpan.FromSeconds(1) };
        var fresh = new CFGOAPInfluenceCell { Intensity = 0.1f, LastVisit = TimeSpan.FromSeconds(2) };

        Assert.That(CFGOAPInfluenceMapSystem.IsBetter(fresh, old));
        Assert.That(CFGOAPInfluenceMapSystem.IsBetter(old, fresh), Is.False);
    }

    [Test]
    public void TheFanIsCentreAndTwoSides()
    {
        var (centre, left, right) = CFGOAPSteeringMath.Fan(new Vector2(2f, 0f), 30f);

        Assert.That(centre, Is.EqualTo(Vector2.UnitX), "The center ray runs along the heading.");
        Assert.That(Vector2.Dot(centre, left), Is.EqualTo(MathF.Cos(MathF.PI / 6f)).Within(0.001f));
        Assert.That(Vector2.Dot(centre, right), Is.EqualTo(MathF.Cos(MathF.PI / 6f)).Within(0.001f));
        Assert.That(left.Y, Is.EqualTo(-right.Y).Within(0.001f), "One to each side.");
        Assert.That(CFGOAPSteeringMath.Fan(Vector2.Zero, 30f).Centre, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void AFeelerLeavesTheSidewaysDirectionsOpen()
    {
        Assert.That(CFGOAPSteeringMath.FeelerSpread(1f), Is.EqualTo(1f).Within(0.001f), "Straight at it is blocked.");
        Assert.That(CFGOAPSteeringMath.FeelerSpread(0.5f), Is.Zero, "60° off is free, so it can slide past.");
        Assert.That(CFGOAPSteeringMath.FeelerSpread(0f), Is.Zero);
    }

    [Test]
    public void TheHazardCircleGrowsWithSpeed()
    {
        var east = new Vector2(4.5f, 0f);
        Assert.That(CFGOAPSteeringMath.HazardRadius(0.6f, Vector2.Zero, Vector2.UnitX), Is.EqualTo(0.6f));
        Assert.That(CFGOAPSteeringMath.HazardRadius(0.6f, east, Vector2.UnitX), Is.GreaterThan(1.2f), "A run carries it about half a tile while it turns.");
        Assert.That(CFGOAPSteeringMath.HazardRadius(0.6f, east, Vector2.UnitY), Is.EqualTo(0.6f).Within(0.001f), "Running alongside lava cannot carry it in.");
        Assert.That(CFGOAPSteeringMath.HazardRadius(0.6f, east, -Vector2.UnitX), Is.EqualTo(0.6f), "Nor can running away from it.");
    }

    [Test]
    public void TheClosestPointIsOnTheSurface()
    {
        var box = new Robust.Shared.Maths.Box2(0f, 0f, 1f, 1f);
        Assert.That(CFGOAPSteeringMath.ClosestPoint(box, new Vector2(-0.5f, 0.5f)), Is.EqualTo(new Vector2(0f, 0.5f)));
        Assert.That(CFGOAPSteeringMath.ClosestPoint(box, new Vector2(2f, 2f)), Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(CFGOAPSteeringMath.ClosestPoint(box, new Vector2(0.5f, 0.5f)), Is.EqualTo(new Vector2(0.5f, 0.5f)), "Inside it, it is the position itself.");
    }

    [Test]
    public void BalancedSidesDefaultToTheRight()
    {
        Assert.That(CFGOAPSteeringMath.TurnSide(1f, 1f), Is.EqualTo(1), "A flat wall hits both sides the same.");
        Assert.That(CFGOAPSteeringMath.TurnSide(0f, 0f), Is.EqualTo(1));
        Assert.That(CFGOAPSteeringMath.TurnSide(1f, 1.05f), Is.EqualTo(1), "Nearly balanced still goes right.");
        Assert.That(CFGOAPSteeringMath.TurnSide(0f, 1f), Is.EqualTo(-1), "It turns away from the worse side.");
        Assert.That(CFGOAPSteeringMath.TurnSide(1f, 0f), Is.EqualTo(1));
    }

    [Test]
    public void FeelersReachFurtherAtSpeed()
    {
        Assert.That(CFGOAPSteeringMath.FeelerReach(2.5f, 4.5f, 4.5f, 1f), Is.EqualTo(2.5f));
        Assert.That(CFGOAPSteeringMath.FeelerReach(2.5f, 9f, 4.5f, 1f), Is.EqualTo(5f), "Twice as fast sees twice as far.");
        Assert.That(CFGOAPSteeringMath.FeelerReach(2.5f, 2.25f, 4.5f, 1f), Is.EqualTo(1.25f));
        Assert.That(CFGOAPSteeringMath.FeelerReach(2.5f, 0f, 4.5f, 1f), Is.EqualTo(1f), "Standing still it can still feel a wall.");
    }

    [Test]
    public void DangerEasesInButSnapsWhenClose()
    {
        const float tick = 1f / 30f;
        var eased = CFGOAPSteeringMath.SmoothDanger(0f, 0.8f, tick);
        Assert.That(eased, Is.GreaterThan(0f).And.LessThan(0.8f), "A ray flicking on does not jump straight to full.");

        var fading = CFGOAPSteeringMath.SmoothDanger(0.8f, 0f, tick);
        Assert.That(fading, Is.GreaterThan(0f).And.LessThan(0.8f), "Nor does it drop straight away.");

        Assert.That(CFGOAPSteeringMath.SmoothDanger(0f, 1.5f, tick), Is.EqualTo(1.5f), "Something about to be touched is not eased.");
    }
}
