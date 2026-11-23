using Content.Shared.CF.Actions;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>When a creature last made an attack or was attacked.</summary>
[RegisterComponent]
public sealed partial class CFFightComponent : Component
{
    public TimeSpan LastFought;

    public bool Fought;
}

/// <summary>
/// Raised on a creature as a fight begins for it: it attacks someone, or someone attacks it, after
/// <see cref="CFFightSystem.Quiet"/> without either. This is what stands in for rolling initiative.
/// </summary>
/// <param name="Opponent">The creature on the other side of that first attack.</param>
/// <param name="Attacking">True for the one making the attack.</param>
/// <param name="OpponentWasQuiet">The opponent had not been fighting either, so it has not acted yet.</param>
[ByRefEvent]
public readonly record struct CFFightStartedEvent(EntityUid Opponent, bool Attacking, bool OpponentWasQuiet);

/// <summary>
/// Real time has no initiative, so a fight begins with the first attack after a quiet spell. <see cref="CFStrikeSystem"/>
/// reports every Strike here. Options that the rules hang on rolling initiative listen for <see cref="CFFightStartedEvent"/>:
/// a Barbarian's Quick-Tempered and a Rogue's Surprise Attack so far.
/// </summary>
public sealed partial class CFFightSystem : EntitySystem
{
    /// <summary>How long without an attack before the next one counts as a new fight.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFActivitySystem _activities = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFGunslingerSystem _gunslinger = default!;

    /// <summary>True if the creature has attacked or been attacked within <see cref="Quiet"/>.</summary>
    public bool IsFighting(EntityUid uid)
    {
        return TryComp(uid, out CFFightComponent? fight) && fight.Fought && _timing.CurTime - fight.LastFought < Quiet;
    }

    /// <summary>An attack is being made. Starts a fight for whichever side was not in one.</summary>
    public void NoteAttack(EntityUid attacker, EntityUid target)
    {
        if (!_net.IsServer || attacker == target || !_mobState.IsAlive(attacker) || !_mobState.IsAlive(target))
            return;

        var attackerWasQuiet = !IsFighting(attacker);
        var targetWasQuiet = !IsFighting(target);
        Touch(attacker);
        Touch(target);

        if (attackerWasQuiet)
        {
            var started = new CFFightStartedEvent(target, true, targetWasQuiet);
            RaiseLocalEvent(attacker, ref started);
        }

        if (targetWasQuiet)
        {
            var started = new CFFightStartedEvent(attacker, false, attackerWasQuiet);
            RaiseLocalEvent(target, ref started);
        }
    }

    private void Touch(EntityUid uid)
    {
        var fight = EnsureComp<CFFightComponent>(uid);
        fight.Fought = true;
        fight.LastFought = _timing.CurTime;
    }

    [SubscribeLocalEvent]
    private void OnFightStarted(Entity<CFCombatOptionsComponent> ent, ref CFFightStartedEvent args)
    {
        // Quick-Tempered: a free Rage as the fight begins, unless in heavy armor or too tired.
        if (ent.Comp is { QuickTempered: true, CanRage: true, Raging: false }
            && _conditions.Value(ent, CFConditionKind.Fatigued) == 0
            && !(_armor.TryGetWornArmor(ent, out var armor) && armor.Comp.Category == CFArmorCategory.Heavy))
        {
            _activities.SetRaging(ent, true);
        }

        // A Gunslinger's initial deed.
        _gunslinger.StartFight(ent);

        // Surprise Attack: whoever has not acted yet is off-guard to the Rogue that starts the fight.
        if (ent.Comp.SurpriseAttack && args.Attacking && args.OpponentWasQuiet)
            _conditions.SetOffGuardTo(args.Opponent, ent, 6f);
    }
}
