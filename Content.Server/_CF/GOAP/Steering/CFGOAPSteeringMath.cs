using System.Numerics;

namespace Content.Server.CF.GOAP.Steering;

/// <summary>
/// The classic steering behaviours as plain math. Each returns the direction to head, or zero if there is none.
/// </summary>
public static class CFGOAPSteeringMath
{
    /// <summary>The cosine of 45°.</summary>
    private const float FeelerSpreadStart = 0.7071f;

    /// <summary>Side rays closer than this count as balanced.</summary>
    private const float SideBalance = 0.1f;

    /// <summary>How fast danger catches up, per second. At 30 ticks a second this takes a few frames.</summary>
    private const float DangerSmoothing = 15f;

    /// <summary>Danger this high skips smoothing, because something is about to be touched.</summary>
    private const float DangerSnap = 1f;

    /// <summary>The time a creature needs to change direction, at the usual acceleration.</summary>
    private const float HazardLeadSeconds = 0.15f;

    public static Vector2 Seek(Vector2 position, Vector2 target)
    {
        var line = target - position;
        return line == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(line);
    }

    public static Vector2 Flee(Vector2 position, Vector2 threat)
    {
        return -Seek(position, threat);
    }

    /// <summary>The share of full speed to use, slowing to a stop inside <paramref name="slowRadius"/>.</summary>
    public static float ArriveSpeed(Vector2 position, Vector2 target, float slowRadius)
    {
        return slowRadius <= 0f ? 1f : Math.Clamp(Vector2.Distance(position, target) / slowRadius, 0f, 1f);
    }

    /// <summary>Where the target will be by the time something at <paramref name="speed"/> could reach it now.</summary>
    public static Vector2 Predict(Vector2 position, float speed, Vector2 target, Vector2 targetVelocity)
    {
        return speed <= 0f ? target : target + targetVelocity * (Vector2.Distance(position, target) / speed);
    }

    public static Vector2 Pursuit(Vector2 position, float speed, Vector2 target, Vector2 targetVelocity)
    {
        return Seek(position, Predict(position, speed, target, targetVelocity));
    }

    public static Vector2 Evade(Vector2 position, float speed, Vector2 threat, Vector2 threatVelocity)
    {
        return Flee(position, Predict(position, speed, threat, threatVelocity));
    }

    /// <summary>The three-ray fan: straight ahead, then one ray to each side.</summary>
    public static (Vector2 Centre, Vector2 Left, Vector2 Right) Fan(Vector2 heading, float degrees)
    {
        var centre = heading == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(heading);
        var side = Angle.FromDegrees(degrees);
        return (centre, side.RotateVec(centre), (-side).RotateVec(centre));
    }

    /// <summary>
    /// How much of a feeler's danger falls on a direction, from how closely the two line up. It stops well short
    /// of 90°, so an obstacle ahead leaves the sideways directions open and a creature slides past it.
    /// </summary>
    public static float FeelerSpread(float alignment)
    {
        return Math.Max(0f, (alignment - FeelerSpreadStart) / (1f - FeelerSpreadStart));
    }

    /// <summary>
    /// How far a feeler reaches at this speed, so a fast creature sees far enough ahead to turn in time.
    /// <paramref name="fullSpeed"/> is a fixed reference, not this creature's own top speed, because a ratio to its
    /// own top speed could never go above 1 and a faster creature would gain nothing.
    /// </summary>
    public static float FeelerReach(float baseLength, float speed, float fullSpeed, float minimum)
    {
        return fullSpeed <= 0f ? baseLength : Math.Max(minimum, baseLength * (speed / fullSpeed));
    }

    /// <summary>
    /// Which way to turn from something straight ahead: away from the side with more danger. When they balance,
    /// they would cancel out and leave it frozen, so it always goes right. 1 is right, and -1 is left.
    /// </summary>
    public static int TurnSide(float leftDanger, float rightDanger)
    {
        return leftDanger + SideBalance < rightDanger ? -1 : 1;
    }

    /// <summary>
    /// Eases danger toward what the feelers report, so a ray flicking on and off a wall does not shake the creature.
    /// High danger applies at once: smoothing must never walk it into lava.
    /// </summary>
    public static float SmoothDanger(float current, float target, float frameTime)
    {
        if (target >= DangerSnap)
            return target;

        return current + (target - current) * Math.Min(1f, frameTime * DangerSmoothing);
    }

    /// <summary>
    /// How far out the safety circle reaches for something that must not be touched. A creature cannot stop at once:
    /// at a run it carries on for about half a tile while it turns, so the circle starts that much sooner.
    /// Only speed toward the hazard counts. Running alongside it can never carry a creature in.
    /// </summary>
    public static float HazardRadius(float radius, Vector2 velocity, Vector2 toHazard)
    {
        if (toHazard == Vector2.Zero)
            return radius;

        var closing = Math.Max(0f, Vector2.Dot(velocity, Vector2.Normalize(toHazard)));
        return radius + closing * HazardLeadSeconds;
    }

    /// <summary>
    /// The closest point of a box to a position, which is the position itself when it is inside.
    /// Measuring to the surface matters: a wall tile's center can be a whole tile away while it is touching.
    /// </summary>
    public static Vector2 ClosestPoint(Box2 box, Vector2 position)
    {
        return new Vector2(Math.Clamp(position.X, box.Left, box.Right), Math.Clamp(position.Y, box.Bottom, box.Top));
    }

    /// <summary>A point on a circle ahead of the creature. Nudging the angle a little each time gives a smooth wander.</summary>
    public static Vector2 Wander(Vector2 heading, float angle, float radius)
    {
        var ahead = heading == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(heading);
        return Seek(Vector2.Zero, ahead * radius * 2f + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
    }
}
