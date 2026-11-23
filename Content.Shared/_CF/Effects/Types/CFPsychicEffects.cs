using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Spells;
using Content.Shared.Mobs.Components;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Forbidden Thought: a lock on the target's mind. The mode picks what is forbidden, a Strike or casting a spell. The first time
/// the target tries it within 6 seconds it takes 2d6 mental damage with a basic Will save, one die more per rank, and stunned 1
/// on a failure when amped. The rules' minute of immunity afterwards is left out.
/// </summary>
public sealed partial class CFForbiddenThoughtEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || target == args.User)
            return;

        var mode = entities.GetComponentOrNull<CFModeChoiceComponent>(args.Source)?.Chosen ?? 0;
        var forbidden = mode == (byte) CFForbiddenAction.CastSpell ? CFForbiddenAction.CastSpell : CFForbiddenAction.Strike;
        var psi = entities.System<CFPsiSystem>();
        psi.Lock(args.User, target, forbidden, 2 + args.HeightenSteps, args.Dc, psi.Amped(args.Source) != null);
    }
}

/// <summary>
/// Amped Figment: the illusion unsettles the nearest enemy beside it, which is off-guard for 6 seconds. The rules give flanking
/// for one melee attack against it; here anyone's attacks have it.
/// </summary>
public sealed partial class CFFigmentFlankEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Position is not { } spot)
            return;

        var lookup = entities.System<EntityLookupSystem>();
        var factions = entities.System<CFFactionSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var here = transform.ToMapCoordinates(spot);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var uid in lookup.GetEntitiesInRange<MobStateComponent>(here, 1.6f))
        {
            if (uid.Owner == args.User || !factions.CanHarm(args.User, uid))
                continue;

            var distance = (transform.GetMapCoordinates(uid).Position - here.Position).Length();
            if (distance >= best)
                continue;

            best = distance;
            nearest = uid;
        }

        if (nearest is { } enemy)
            entities.System<CFConditionSystem>().SetOffGuardAll(enemy, DurationSeconds);
    }
}
