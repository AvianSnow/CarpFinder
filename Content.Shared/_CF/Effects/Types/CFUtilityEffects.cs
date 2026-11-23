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

    /// <summary>Only for these modes of the source's <c>CFModeChoice</c>. Empty means every mode.</summary>
    [DataField]
    public List<byte> OnlyModes = new();

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target && target != args.User && args.ModeFits(OnlyModes))
            entities.System<CFConditionSystem>().Push(args.User, target, Feet, Pull);
    }
}

/// <summary>
/// For a while the user can leave an enemy's reach at no cost and without setting off a reaction: a free Step, or a Stride
/// that does not provoke.
/// </summary>
public sealed partial class CFFreeStepEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 2f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFReactionSystem>().CoverSteps(args.User, DurationSeconds);
    }
}

/// <summary>The user ignores every difficult terrain for a while (Light Paws).</summary>
public sealed partial class CFIgnoreTerrainEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<Content.Shared.CF.Terrain.CFTerrainSystem>().IgnoreAllFor(args.User, DurationSeconds);
    }
}

/// <summary>The user jumps the way it faces, this many feet.</summary>
public sealed partial class CFLeapEffect : CFEffect
{
    [DataField(required: true)]
    public int Feet;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<Content.Shared.CF.Actions.CFActivitySystem>().LeapBy(args.User, Feet / CFMelee.FeetPerTile);
    }
}

/// <summary>A free Recall Knowledge about the target, as part of another action (Combat Assessment).</summary>
public sealed partial class CFRecallEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target && target != args.User)
            entities.System<Content.Shared.CF.Actions.CFManeuverSystem>().TryRecall(args.User, target, free: true);
    }
}

/// <summary>The user grabs the target with no check, as Combat Grab does after its Strike. It lets go after a while.</summary>
public sealed partial class CFGrabEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || target == args.User)
            return;

        entities.System<CFConditionSystem>().ApplyGrabbed(target, args.User, DurationSeconds);
    }
}

/// <summary>
/// The user rolls Athletics against the target's Reflex DC to knock it prone, as a Trip. A critical success also deals
/// 1d6 bludgeoning damage, and a critical failure knocks the user prone.
/// </summary>
public sealed partial class CFTripEffect : CFEffect
{
    /// <summary>A success counts as a critical success (Flying Tackle).</summary>
    [DataField]
    public bool SuccessIsCritical;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || target == args.User)
            return;

        var conditions = entities.System<CFConditionSystem>();
        var degree = conditions.RollSkill(args.User, target, "Athletics", Content.Shared.CF.Preferences.CFAbility.Strength, Content.Shared.CF.Checks.CFDefenseType.Reflex, attackTrait: true);
        if (SuccessIsCritical && degree == Content.Shared.CF.Checks.CFDegree.Success)
            degree = Content.Shared.CF.Checks.CFDegree.CriticalSuccess;

        switch (degree)
        {
            case Content.Shared.CF.Checks.CFDegree.CriticalSuccess:
                conditions.KnockProne(target);
                entities.System<CFEffectSystem>().DealDamage(args.User, target, entities.System<CFEffectSystem>().Roll(1, 6, 0), "Bludgeoning", critical: false);
                break;
            case Content.Shared.CF.Checks.CFDegree.Success:
                conditions.KnockProne(target);
                break;
            case Content.Shared.CF.Checks.CFDegree.CriticalFailure:
                conditions.KnockProne(args.User);
                break;
        }
    }
}

/// <summary>A dying creature loses the dying condition and wakes at 1 HP.</summary>
public sealed partial class CFStabilizeEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFDyingSystem>().Stabilize(target, args.User);
    }
}

/// <summary>Spawns an entity: a visual, a hazard, or a summoned object.</summary>
public sealed partial class CFSpawnEffect : CFEffect
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public CFEffectAnchor At = CFEffectAnchor.Target;

    [DataField]
    public int Count = 1;

    /// <summary>One more for each step the source is heightened (Cornucopia).</summary>
    [DataField]
    public int HeightenCount;

    /// <summary>Goes into the user's free hand, and stays where it appeared when there is none.</summary>
    [DataField]
    public bool ToHand;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (CFEffectAnchors.Resolve(entities, args, At) is not { } coords)
            return;

        var count = Count + HeightenCount * args.HeightenSteps;
        for (var i = 0; i < count; i++)
        {
            var spawned = entities.SpawnAttachedTo(Prototype, coords);
            if (ToHand)
                entities.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(args.User, spawned, animate: false);
        }
    }
}

/// <summary>
/// Calls a creature to fight for the user, at the aimed spot or beside the user, for a while. The spell's rank picks
/// which: the last entry of <see cref="Creatures"/> it reaches. The creature is bound to the user as a companion, so it
/// fights the user's enemies and can be pointed at a target. It stays its time with no input, as every sustained spell
/// here does, and only a dispel sends it back sooner.
/// </summary>
public sealed partial class CFSummonEffect : CFEffect
{
    [DataField(required: true)]
    public List<Content.Shared.CF.Companions.CFSummonOption> Creatures = new();

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        EntProtoId? pick = null;
        var reached = int.MinValue;
        foreach (var option in Creatures)
        {
            if (option.Rank > args.Rank || option.Rank < reached)
                continue;

            reached = option.Rank;
            pick = option.Prototype;
        }

        if (pick is not { } prototype)
            return;

        var where = args.Position ?? entities.GetComponent<TransformComponent>(args.User).Coordinates;
        var request = new Content.Shared.CF.Companions.CFSummonRequestEvent(args.User, prototype, where, DurationSeconds, args.Rank, args.Dc);
        entities.EventBus.RaiseEvent(EventSource.Local, ref request);
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

/// <summary>Kills the target outright, as the last stage of a poison.</summary>
public sealed partial class CFKillEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFDyingSystem>().Kill(target, args.User == target ? null : args.User);
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
