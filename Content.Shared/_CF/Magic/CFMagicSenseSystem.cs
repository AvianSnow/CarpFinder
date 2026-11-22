using System.Numerics;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Items;
using Content.Shared.CF.Traits;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Magic;

/// <summary>An orb from the Light cantrip. It floats with whatever it is attached to.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFLightOrbComponent : Component
{
    [DataField]
    public EntityUid Caster;

    [DataField]
    public TimeSpan Created;
}

/// <summary>What a Detect Magic pulse found.</summary>
public readonly record struct CFMagicPulse(int Count, EntityUid? Strongest, int Level);

/// <summary>
/// The Light, Detect Magic and Read Aura cantrips. These replace CP14's magic lanterns, mana vision and aura imprints.
/// </summary>
public sealed partial class CFMagicSenseSystem : EntitySystem
{
    /// <summary>Detect Magic is a 30-foot emanation, and a tile is 5 feet.</summary>
    public const float PulseTiles = 6f;

    /// <summary>Only four Light spells can be active at once.</summary>
    public const int MaxOrbs = 4;

    /// <summary>Bright light for 20 feet and dim for 20 more, in tiles.</summary>
    public const float OrbRadius = 8f;

    /// <summary>Heightened (4th): 60 feet and 60 more.</summary>
    public const float HeightenedOrbRadius = 24f;

    private static readonly ProtoId<CFTraitPrototype> MagicalTrait = "Magical";
    private static readonly ProtoId<TagPrototype> MagicalTag = "CFMagical";
    private static readonly EntProtoId Orb = "CFLightOrb";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;

    public bool IsMagical(EntityUid uid)
    {
        if (TryComp(uid, out CFTraitComponent? traits) && traits.HasTrait(MagicalTrait))
            return true;

        if (_tags.HasTag(uid, MagicalTag))
            return true;

        if (TryComp(uid, out CFWeaponRunesComponent? weapon)
            && (weapon.Potency > 0 || weapon.Striking > 0 || weapon.Property.Count > 0))
        {
            return true;
        }

        return TryComp(uid, out CFArmorRunesComponent? armor) && (armor.Potency > 0 || armor.Resilient > 0);
    }

    /// <summary>
    /// Everything magical within 30 feet of the user, skipping what the user carries and their own lights:
    /// the spell lets you ignore magic you already know about.
    /// </summary>
    public CFMagicPulse Pulse(EntityUid user)
    {
        var count = 0;
        var level = -1;
        EntityUid? strongest = null;

        var nearby = _lookup.GetEntitiesInRange(user, PulseTiles, LookupFlags.Uncontained | LookupFlags.Contained);
        foreach (var uid in nearby)
        {
            if (!IsMagical(uid) || IsOwn(user, uid))
                continue;

            count++;
            var itemLevel = CompOrNull<CFItemComponent>(uid)?.Level ?? 0;
            if (itemLevel <= level)
                continue;

            level = itemLevel;
            strongest = uid;
        }

        return new CFMagicPulse(count, strongest, Math.Max(0, level));
    }

    private bool IsOwn(EntityUid user, EntityUid uid)
    {
        if (TryComp(uid, out CFLightOrbComponent? orb) && orb.Caster == user)
            return true;

        return _transform.ContainsEntity(user, uid);
    }

    /// <summary>
    /// Casts Detect Magic. Rank 1 and 2 learn only that magic is here. Rank 3 also learns the strongest level.
    /// Rank 4 also narrows down where it is.
    /// </summary>
    public void CastDetectMagic(EntityUid user, int rank)
    {
        var pulse = Pulse(user);
        if (pulse.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-magic-none"), user, user);
            return;
        }

        if (rank < 3)
        {
            _popup.PopupEntity(Loc.GetString("cf-magic-present"), user, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString("cf-magic-level", ("level", pulse.Level)), user, user);
        if (rank >= 4 && pulse.Strongest is { } source)
            _popup.PopupCoordinates(Loc.GetString("cf-magic-source"), Transform(source).Coordinates, user, PopupType.Medium);
    }

    public void CastReadAura(EntityUid user, EntityUid target)
    {
        var text = IsMagical(target) ? "cf-magic-aura-magical" : "cf-magic-aura-mundane";
        _popup.PopupEntity(Loc.GetString(text, ("target", target)), target, user);
    }

    /// <summary>
    /// Casts Light on a creature or object. Casting it on your own light dismisses it.
    /// A fifth light ends your oldest. Returns the orb, or null if the cast only dismissed one.
    /// </summary>
    public EntityUid? CastLight(EntityUid user, EntityUid target, int rank)
    {
        EntityUid? oldest = null;
        var oldestTime = TimeSpan.MaxValue;
        var count = 0;

        var orbs = EntityQueryEnumerator<CFLightOrbComponent, TransformComponent>();
        while (orbs.MoveNext(out var uid, out var orb, out var xform))
        {
            if (orb.Caster != user)
                continue;

            if (xform.ParentUid == target)
            {
                Del(uid);
                return null;
            }

            count++;
            if (orb.Created >= oldestTime)
                continue;

            oldest = uid;
            oldestTime = orb.Created;
        }

        if (count >= MaxOrbs && oldest is { } expired)
            Del(expired);

        var light = SpawnAttachedTo(Orb, new EntityCoordinates(target, Vector2.Zero));
        var comp = EnsureComp<CFLightOrbComponent>(light);
        comp.Caster = user;
        comp.Created = _timing.CurTime;

        _lights.SetRadius(light, rank >= 4 ? HeightenedOrbRadius : OrbRadius);
        return light;
    }
}
