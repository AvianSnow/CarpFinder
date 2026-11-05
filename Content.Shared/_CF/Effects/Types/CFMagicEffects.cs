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

/// <summary>Light: an orb attached to the target.</summary>
public sealed partial class CFLightEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFMagicSenseSystem>().CastLight(args.User, target, args.Rank);
    }
}
