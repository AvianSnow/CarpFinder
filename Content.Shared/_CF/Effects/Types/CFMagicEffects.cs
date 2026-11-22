using Content.Shared.CF.Magic;

namespace Content.Shared.CF.Effects;

/// <summary>Detect Magic: a pulse around the user.</summary>
public sealed partial class CFDetectMagicEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFMagicSenseSystem>().CastDetectMagic(args.User, args.Rank);
    }
}

/// <summary>Read Aura: whether the target is magical.</summary>
public sealed partial class CFReadAuraEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFMagicSenseSystem>().CastReadAura(args.User, target);
    }
}

/// <summary>
/// Dispel Magic: counteracts one spell on the target. The counteract rank is the slot's rank, and the check is the spell
/// attack modifier, which is the spell DC less 10.
/// </summary>
public sealed partial class CFDispelEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target
            && !entities.System<CFCounteractSystem>().TryDispel(args.User, target, Math.Max(1, args.Rank), args.Dc - 10))
        {
            entities.System<Content.Shared.Popups.SharedPopupSystem>().PopupEntity(Loc.GetString("cf-spell-nothing-to-dispel"), target, args.User);
        }
    }
}

/// <summary>Sigil: a glowing rune on the floor at the aimed spot. The server paints it, as a crayon would.</summary>
public sealed partial class CFSigilEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Position is not { } spot)
            return;

        var ev = new CFPlaceSigilEvent(args.User, spot);
        entities.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }
}

/// <summary>Figment: the image of a random item at the aimed spot.</summary>
public sealed partial class CFFigmentEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Position is { } spot)
            entities.System<CFFigmentSystem>().Conjure(args.User, spot);
    }
}

/// <summary>
/// Know the Way: an arrow a tile north of the user, pointing north. It turns with the world, so it is right on a rotated
/// screen too, where north is easy to lose.
/// </summary>
public sealed partial class CFKnowTheWayEffect : CFEffect
{
    /// <summary>The arrow's sprite points down, which is south.</summary>
    private static readonly Angle PointNorth = Angle.FromDegrees(180);

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var transform = entities.System<SharedTransformSystem>();
        var at = transform.GetMapCoordinates(args.User).Offset(new System.Numerics.Vector2(0f, 1f));
        var arrow = entities.Spawn("CFNorthArrow", at);
        transform.SetWorldRotation(arrow, PointNorth);
    }
}

/// <summary>Light: an orb attached to the target.</summary>
public sealed partial class CFLightEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFMagicSenseSystem>().CastLight(args.User, target, args.Rank);
    }
}
