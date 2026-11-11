using Content.Shared.CF.Companions;
using Content.Shared.CF.Traits;
using Content.Shared.Mobs;

namespace Content.Server.CF.Companions;

/// <summary>
/// Summoned creatures carry the Summoned trait, and vanish in a puff of smoke when they die or their time runs out.
/// </summary>
public sealed partial class CFSummonedSystem : EntitySystem
{
    private const string Trait = "Summoned";

    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFSummonedComponent> ent, ref MapInitEvent args)
    {
        var traits = EnsureComp<CFTraitComponent>(ent);
        if (traits.Traits.Contains(Trait))
            return;

        traits.Traits.Add(Trait);
        Dirty(ent, traits);
    }

    // MobStateComponent + MobStateChangedEvent belongs to SharedStunSystem, so this listens to the broadcast.
    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && HasComp<CFSummonedComponent>(args.Target))
            Unsummon(args.Target);
    }

    /// <summary>Sends a summoned creature back where it came from, leaving a puff of smoke.</summary>
    public void Unsummon(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out CFSummonedComponent? summoned))
            return;

        SpawnAtPosition(summoned.Puff, _transform.GetMoverCoordinates(uid));
        QueueDel(uid);
    }
}
