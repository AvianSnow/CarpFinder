using Content.Shared.CF.Combat;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>Where a cosmetic or movement effect happens.</summary>
public enum CFEffectAnchor : byte
{
    Target,

    /// <summary>The aimed point, or the target's position when there is no point.</summary>
    Position,
    User,
}

public static class CFEffectAnchors
{
    public static EntityCoordinates? Resolve(IEntityManager entities, CFEffectArgs args, CFEffectAnchor anchor)
    {
        return anchor switch
        {
            CFEffectAnchor.User => entities.GetComponent<TransformComponent>(args.User).Coordinates,
            CFEffectAnchor.Position when args.Position != null => args.Position,
            _ when args.Target is { } target => entities.GetComponent<TransformComponent>(target).Coordinates,
            _ => args.Position,
        };
    }
}

/// <summary>Push the target away from the user, or pull it toward them, by a number of feet.</summary>
public sealed partial class CFForcedMovementEffect : CFEffect
{
    [DataField]
    public int Feet = 5;

    /// <summary>True pulls toward the user instead of pushing away.</summary>
    [DataField]
    public bool Pull;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target && target != args.User)
            entities.System<CFConditionSystem>().Push(args.User, target, Feet, Pull);
    }
}

/// <summary>A dying creature loses the dying condition and stays unconscious at 0 HP.</summary>
public sealed partial class CFStabilizeEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFDyingSystem>().Stabilize(target);
    }
}

/// <summary>Spawns an entity: a visual, a hazard, or a summoned object.</summary>
public sealed partial class CFSpawnEffect : CFEffect
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public CFEffectAnchor At = CFEffectAnchor.Target;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (CFEffectAnchors.Resolve(entities, args, At) is { } coords)
            entities.SpawnEntity(Prototype, coords);
    }
}

public sealed partial class CFSoundEffect : CFEffect
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField]
    public CFEffectAnchor At = CFEffectAnchor.Target;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (CFEffectAnchors.Resolve(entities, args, At) is { } coords)
            entities.System<SharedAudioSystem>().PlayPvs(Sound, coords);
    }
}

/// <summary>A line of text over the target or the user.</summary>
public sealed partial class CFPopupEffect : CFEffect
{
    /// <summary>Locale id.</summary>
    [DataField(required: true)]
    public string Text = "";

    [DataField]
    public CFEffectAnchor At = CFEffectAnchor.Target;

    /// <summary>Only the user sees it.</summary>
    [DataField]
    public bool UserOnly;

    [DataField]
    public PopupType Type = PopupType.Small;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var over = At == CFEffectAnchor.User ? args.User : args.Target ?? args.User;
        var popups = entities.System<SharedPopupSystem>();
        if (UserOnly)
            popups.PopupEntity(Loc.GetString(Text), over, args.User, Type);
        else
            popups.PopupEntity(Loc.GetString(Text), over, Type);
    }
}

/// <summary>
/// Runs effects on every creature in an area around the target or point, with no new check.
/// A bomb's splash, or a spell that hits one creature and everything next to it.
/// </summary>
public sealed partial class CFNestedAreaEffect : CFEffect
{
    [DataField]
    public CFAreaShape Shape = CFAreaShape.Emanation;

    [DataField]
    public int SizeFeet = 5;

    /// <summary>The creature at the center is affected too (splash hits the primary target).</summary>
    [DataField]
    public bool IncludeCenter = true;

    /// <summary>The user can be caught in it.</summary>
    [DataField]
    public bool IncludeSelf;

    [DataField]
    public CFTargetFilter Filter = CFTargetFilter.All;

    [DataField]
    public int MaxTargets;

    [DataField(required: true)]
    public List<CFEffect> Effects = new();

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var transform = entities.System<SharedTransformSystem>();
        MapCoordinates center;
        if (args.Target is { } target)
            center = transform.GetMapCoordinates(target);
        else if (args.Position is { } position)
            center = transform.ToMapCoordinates(position);
        else
            return;

        var system = entities.System<CFEffectSystem>();
        var targets = system.CollectArea(
            args.User,
            Shape,
            SizeFeet,
            center,
            center,
            atPoint: true,
            includeCenter: IncludeCenter,
            selfAllowed: IncludeSelf,
            Filter,
            maxTargets: MaxTargets);

        foreach (var uid in targets)
        {
            if (!IncludeCenter && uid == args.Target)
                continue;

            var nested = args with
            {
                Target = uid,
                Position = entities.GetComponent<TransformComponent>(uid).Coordinates,
            };
            system.Run(Effects, nested);
        }
    }
}
