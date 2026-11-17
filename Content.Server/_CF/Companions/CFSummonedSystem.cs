using Content.Shared.CF.Combat;
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
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;
    [Dependency] private CFCompanionSystem _companions = default!;
    [Dependency] private Content.Shared.CF.Effects.CFBonusSystem _bonuses = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFSummonedComponent>();
        while (query.MoveNext(out var uid, out var summoned))
        {
            if (summoned.Until != TimeSpan.Zero && now >= summoned.Until)
                Unsummon(uid);
        }
    }

    /// <summary>
    /// A summoning spell: the creature appears, takes its caster's side and stays for a while. The caster's last such
    /// creature goes back first, which stands in for having to Sustain the spell.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSummonRequest(ref CFSummonRequestEvent args)
    {
        var old = EntityQueryEnumerator<CFSummonedComponent, CFCompanionComponent>();
        while (old.MoveNext(out var uid, out var was, out var companion))
        {
            if (was.FromSpell && companion.Summoner == args.Summoner)
                Unsummon(uid);
        }

        var creature = SpawnAtPosition(args.Prototype, args.Where);
        var summoned = EnsureComp<CFSummonedComponent>(creature);
        summoned.FromSpell = true;
        summoned.Until = args.Seconds > 0f ? _timing.CurTime + TimeSpan.FromSeconds(args.Seconds) : TimeSpan.Zero;
        _companions.Bind(args.Summoner, creature);
        args.Summoned = creature;

        // Thoughtform Summoning: resistance to physical damage equal to half its level, weakness 5 to force and mental.
        if (CompOrNull<CFCombatOptionsComponent>(args.Summoner)?.ThoughtformSummoning != true)
            return;

        var half = Math.Max(1, (CompOrNull<CFCombatantComponent>(creature)?.Level ?? 1) / 2);
        var lasts = args.Seconds > 0f ? args.Seconds : 3600f;
        foreach (var type in new[] { "Bludgeoning", "Piercing", "Slashing" })
        {
            _bonuses.AddResistance(creature, type, half, weakness: false, lasts);
        }

        _bonuses.AddResistance(creature, "Force", 5, weakness: true, lasts);
        _bonuses.AddResistance(creature, "Mental", 5, weakness: true, lasts);
    }

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
