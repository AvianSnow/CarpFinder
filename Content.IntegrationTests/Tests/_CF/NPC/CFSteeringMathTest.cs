using System.Numerics;
using Content.Server.CF.NPC;

namespace Content.IntegrationTests.Tests.CF.NPC;

/// <summary>The steering behaviours as plain math. These need no game running.</summary>
[TestFixture]
[TestOf(typeof(CFSteering))]
public sealed class CFSteeringMathTest
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
                ? CFSteering.Pursuit(chaser, speed, target, velocity)
                : CFSteering.Seek(chaser, target);

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
        Assert.That(CFSteering.Flee(Vector2.Zero, threat), Is.EqualTo(new Vector2(-1f, 0f)));
        Assert.That(CFSteering.Evade(Vector2.Zero, 1f, threat, Vector2.Zero), Is.EqualTo(new Vector2(-1f, 0f)));
        Assert.That(CFSteering.Seek(Vector2.Zero, Vector2.Zero), Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void ArriveSlowsInsideItsRadius()
    {
        var target = new Vector2(10f, 0f);
        Assert.That(CFSteering.ArriveSpeed(Vector2.Zero, target, 2f), Is.EqualTo(1f));
        Assert.That(CFSteering.ArriveSpeed(new Vector2(9f, 0f), target, 2f), Is.EqualTo(0.5f));
        Assert.That(CFSteering.ArriveSpeed(target, target, 2f), Is.Zero);
    }

    [Test]
    public void WanderStaysRoughlyAhead()
    {
        for (var angle = 0f; angle < MathF.Tau; angle += 0.5f)
        {
            Assert.That(Vector2.Dot(CFSteering.Wander(Vector2.UnitX, angle, 1f), Vector2.UnitX), Is.GreaterThan(0f));
        }
    }

    [Test]
    public void ANewerMarkBeatsAStrongerOne()
    {
        var old = new CFInfluenceCell { Intensity = 1f, LastVisit = TimeSpan.FromSeconds(1) };
        var fresh = new CFInfluenceCell { Intensity = 0.1f, LastVisit = TimeSpan.FromSeconds(2) };

        Assert.That(CFInfluenceMapSystem.IsBetter(fresh, old));
        Assert.That(CFInfluenceMapSystem.IsBetter(old, fresh), Is.False);
    }

    [Test]
    public void TheFanIsCentreAndTwoSides()
    {
        var (centre, left, right) = CFSteering.Fan(new Vector2(2f, 0f), 30f);

        Assert.That(centre, Is.EqualTo(Vector2.UnitX), "The center ray runs along the heading.");
        Assert.That(Vector2.Dot(centre, left), Is.EqualTo(MathF.Cos(MathF.PI / 6f)).Within(0.001f));
        Assert.That(Vector2.Dot(centre, right), Is.EqualTo(MathF.Cos(MathF.PI / 6f)).Within(0.001f));
        Assert.That(left.Y, Is.EqualTo(-right.Y).Within(0.001f), "One to each side.");
        Assert.That(CFSteering.Fan(Vector2.Zero, 30f).Centre, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void AFeelerLeavesTheSidewaysDirectionsOpen()
    {
        Assert.That(CFSteering.FeelerSpread(1f), Is.EqualTo(1f).Within(0.001f), "Straight at it is blocked.");
        Assert.That(CFSteering.FeelerSpread(0.5f), Is.Zero, "60° off is free, so it can slide past.");
        Assert.That(CFSteering.FeelerSpread(0f), Is.Zero);
    }

    [Test]
    public void TheHazardCircleGrowsWithSpeed()
    {
        var east = new Vector2(4.5f, 0f);
        Assert.That(CFSteering.HazardRadius(0.6f, Vector2.Zero, Vector2.UnitX), Is.EqualTo(0.6f));
        Assert.That(CFSteering.HazardRadius(0.6f, east, Vector2.UnitX), Is.GreaterThan(1.2f), "A run carries it about half a tile while it turns.");
        Assert.That(CFSteering.HazardRadius(0.6f, east, Vector2.UnitY), Is.EqualTo(0.6f).Within(0.001f), "Running alongside lava cannot carry it in.");
        Assert.That(CFSteering.HazardRadius(0.6f, east, -Vector2.UnitX), Is.EqualTo(0.6f), "Nor can running away from it.");
    }

    [Test]
    public void TheClosestPointIsOnTheSurface()
    {
        var box = new Robust.Shared.Maths.Box2(0f, 0f, 1f, 1f);
        Assert.That(CFSteering.ClosestPoint(box, new Vector2(-0.5f, 0.5f)), Is.EqualTo(new Vector2(0f, 0.5f)));
        Assert.That(CFSteering.ClosestPoint(box, new Vector2(2f, 2f)), Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(CFSteering.ClosestPoint(box, new Vector2(0.5f, 0.5f)), Is.EqualTo(new Vector2(0.5f, 0.5f)), "Inside it, it is the position itself.");
    }

    [Test]
    public void BalancedSidesDefaultToTheRight()
    {
        Assert.That(CFSteering.TurnSide(1f, 1f), Is.EqualTo(1), "A flat wall hits both sides the same.");
        Assert.That(CFSteering.TurnSide(0f, 0f), Is.EqualTo(1));
        Assert.That(CFSteering.TurnSide(1f, 1.05f), Is.EqualTo(1), "Nearly balanced still goes right.");
        Assert.That(CFSteering.TurnSide(0f, 1f), Is.EqualTo(-1), "It turns away from the worse side.");
        Assert.That(CFSteering.TurnSide(1f, 0f), Is.EqualTo(1));
    }

    [Test]
    public void FeelersReachFurtherAtSpeed()
    {
        Assert.That(CFSteering.FeelerReach(2.5f, 4.5f, 4.5f, 1f), Is.EqualTo(2.5f));
        Assert.That(CFSteering.FeelerReach(2.5f, 9f, 4.5f, 1f), Is.EqualTo(5f), "Twice as fast sees twice as far.");
        Assert.That(CFSteering.FeelerReach(2.5f, 2.25f, 4.5f, 1f), Is.EqualTo(1.25f));
        Assert.That(CFSteering.FeelerReach(2.5f, 0f, 4.5f, 1f), Is.EqualTo(1f), "Standing still it can still feel a wall.");
    }

    [Test]
    public void DangerEasesInButSnapsWhenClose()
    {
        const float tick = 1f / 30f;
        var eased = CFSteering.SmoothDanger(0f, 0.8f, tick);
        Assert.That(eased, Is.GreaterThan(0f).And.LessThan(0.8f), "A ray flicking on does not jump straight to full.");

        var fading = CFSteering.SmoothDanger(0.8f, 0f, tick);
        Assert.That(fading, Is.GreaterThan(0f).And.LessThan(0.8f), "Nor does it drop straight away.");

        Assert.That(CFSteering.SmoothDanger(0f, 1.5f, tick), Is.EqualTo(1.5f), "Something about to be touched is not eased.");
    }
}
