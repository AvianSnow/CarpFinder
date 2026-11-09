using System.Numerics;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Aim assist for a left-click swing. A swing in the general direction of an enemy in reach hits the closest one, so
/// the cursor does not have to be exactly on it. A click on an enemy in reach still hits that enemy.
/// </summary>
public sealed partial class CFMeleeAimSystem : EntitySystem
{
    /// <summary>Cosine of how far off the aim a creature can be: 60 degrees to either side.</summary>
    private const float ConeCosine = 0.5f;

    /// <summary>The reach check is surface to surface, so a body's centre can be a little past the weapon's range.</summary>
    private const float BodyAllowance = 0.5f;

    /// <summary>A cursor this close to the attacker gives no direction, so any side counts.</summary>
    private const float NoDirectionSquared = 0.01f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    /// <summary>The enemy this swing should hit instead of what the cursor is on. False if the swing needs no help.</summary>
    /// <param name="clicked">What the cursor is on, if anything.</param>
    public bool TryPick(EntityUid user, EntityUid? clicked, MapCoordinates aim, float range, out EntityUid target)
    {
        target = default;
        if (clicked is { } direct && IsFoe(user, direct) && _interaction.InRangeUnobstructed(user, direct, range))
            return false;

        var origin = _transform.GetMapCoordinates(user);
        if (origin.MapId != aim.MapId)
            return false;

        var aimAt = aim.Position - origin.Position;
        var anySide = aimAt.LengthSquared() < NoDirectionSquared;
        var aimDirection = anySide ? Vector2.Zero : Vector2.Normalize(aimAt);

        var best = float.MaxValue;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(origin, range + BodyAllowance))
        {
            if (!IsFoe(user, mob))
                continue;

            var offset = _transform.GetMapCoordinates(mob).Position - origin.Position;
            var distance = offset.LengthSquared();
            if (distance >= best)
                continue;

            // Two bodies on one spot have no direction between them, and Normalize would give NaN.
            if (!anySide && distance > 0f && Vector2.Dot(aimDirection, Vector2.Normalize(offset)) < ConeCosine)
                continue;

            if (!_interaction.InRangeUnobstructed(user, mob.Owner, range))
                continue;

            best = distance;
            target = mob;
        }

        return best < float.MaxValue;
    }

    /// <summary>
    /// A living creature it would make sense to swing at. Neutral creatures, such as merchants, have to be clicked,
    /// and a hidden one must not be given away by the aim snapping to it.
    /// </summary>
    private bool IsFoe(EntityUid user, EntityUid creature)
    {
        if (!HasComp<MobStateComponent>(creature) || !_mobState.IsAlive(creature) || !_factions.CanHarm(user, creature))
            return false;

        if (HasComp<CFFactionComponent>(user)
            && HasComp<CFFactionComponent>(creature)
            && _factions.GetAlliance(user, creature) == CFAlliance.Neutral)
        {
            return false;
        }

        return !_stealth.IsUnseenBy(creature, user);
    }
}
