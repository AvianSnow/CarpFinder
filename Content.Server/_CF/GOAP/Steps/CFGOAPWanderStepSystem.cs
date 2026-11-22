using System.Numerics;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Idles the way an animal does: picks a spot a few tiles off, walks there, slows down as it arrives, stands a while,
/// then picks another. Spots stay inside its home range, and only ones it can walk straight to are picked, so it never
/// needs a path. How long it rests and how far it goes come from its personality.
/// </summary>
public sealed partial class CFGOAPWanderStepSystem : CFGOAPStepSystem
{
    /// <summary>The shortest idle walk, in tiles. Anything shorter looks like a twitch.</summary>
    private const float MinWalk = 1.5f;

    /// <summary>How many spots it tries before resting where it is.</summary>
    private const int Tries = 6;

    /// <summary>Within this of the spot, in tiles, it has got there.</summary>
    private const float Arrival = 0.3f;

    /// <summary>
    /// Past this share of its home range it only picks spots back toward home, so it drifts home instead of walking out
    /// of range and being sent back.
    /// </summary>
    private const float HomeBias = 0.6f;

    /// <summary>How far either side of the way home such a spot can be, in radians.</summary>
    private const float HomeSpread = MathF.PI / 3f;

    /// <summary>How long a walk may take per tile before it gives up and rests where it is, in seconds.</summary>
    private const float SecondsPerTile = 1.5f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Wander;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // A short look around first, so a creature that just lost interest in something does not set off at once.
        ent.Comp.WanderPoint = null;
        ent.Comp.WanderRestUntil = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0.5f, 1.5f));
        Steering.Stop(ent.Owner);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        var now = _timing.CurTime;
        var here = _transform.GetMapCoordinates(ent);
        if (ent.Comp.WanderPoint is { } point)
        {
            if (Vector2.Distance(here.Position, point) <= Arrival
                || now >= ent.Comp.WanderGiveUpAt
                || TryComp(ent, out CFGOAPSteeringComponent? steering) && steering.Settled)
                Rest(ent, now);

            return false;
        }

        if (now < ent.Comp.WanderRestUntil)
            return false;

        if (!TryPickSpot(ent, here, out var spot))
        {
            Rest(ent, now);
            return false;
        }

        ent.Comp.WanderPoint = spot;
        ent.Comp.WanderGiveUpAt = now + TimeSpan.FromSeconds(Vector2.Distance(here.Position, spot) * SecondsPerTile + 1f);
        Steering.MoveTo(ent, spot, CFGOAPFeltMobs.Ignore);
        return false;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        ent.Comp.WanderPoint = null;
    }

    private void Rest(Entity<CFGOAPComponent> ent, TimeSpan now)
    {
        var profile = Goap.Profile(ent);
        ent.Comp.WanderPoint = null;
        ent.Comp.WanderRestUntil = now + TimeSpan.FromSeconds(_random.NextFloat(profile.WanderRestMin, MathF.Max(profile.WanderRestMin, profile.WanderRestMax)));
        Steering.Stop(ent.Owner);
    }

    /// <summary>A spot it can stand on and walk straight to, inside its home range.</summary>
    private bool TryPickSpot(Entity<CFGOAPComponent> ent, MapCoordinates here, out Vector2 spot)
    {
        spot = default;
        var profile = Goap.Profile(ent);
        var longest = MathF.Max(MinWalk, profile.WanderDistance);
        var hasHome = Goap.TryGetHome(ent, out var home);
        var toHome = hasHome ? home.Position - here.Position : Vector2.Zero;
        var homeward = hasHome && toHome.Length() > ent.Comp.HomeRange * HomeBias;

        for (var i = 0; i < Tries; i++)
        {
            var angle = homeward
                ? MathF.Atan2(toHome.Y, toHome.X) + _random.NextFloat(-HomeSpread, HomeSpread)
                : _random.NextFloat(0f, MathF.Tau);
            var length = _random.NextFloat(MinWalk, longest);
            var candidate = here.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;

            if (hasHome && Vector2.Distance(candidate, home.Position) > ent.Comp.HomeRange)
                continue;

            if (!Steering.IsWalkable(ent, new MapCoordinates(candidate, here.MapId)) || !Steering.CanWalkStraight(ent, candidate))
                continue;

            spot = candidate;
            return true;
        }

        return false;
    }
}
