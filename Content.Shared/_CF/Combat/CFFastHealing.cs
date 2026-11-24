using Content.Shared.CF.Companions;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Regains Hit Points every 6 seconds. A spell gives it for a while (Lifelink Surge); a monster has it in YAML with
/// no end, as fast healing or as regeneration that some damage switches off.
/// </summary>
[RegisterComponent]
public sealed partial class CFFastHealingComponent : Component
{
    [DataField]
    public int PerPulse;

    /// <summary>When it ends. Zero is never, for a creature that has it by nature.</summary>
    [DataField]
    public TimeSpan Until;

    [DataField]
    public TimeSpan Next;

    /// <summary>Damage of one of these types stops the healing for 6 seconds, as fire stops a hydra's.</summary>
    [DataField]
    public List<ProtoId<CFDamageTypePrototype>> DeactivatedBy = new();

    [DataField]
    public TimeSpan StoppedUntil;
}

public sealed partial class CFFastHealingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFFastHealingComponent>();
        while (query.MoveNext(out var uid, out var healing))
        {
            if (healing.Until != TimeSpan.Zero && now >= healing.Until)
            {
                RemCompDeferred<CFFastHealingComponent>(uid);
                continue;
            }

            if (now < healing.Next)
                continue;

            healing.Next = now + CFEidolonSystem.Pulse;
            if (now < healing.StoppedUntil || healing.PerPulse <= 0 || _mobState.IsDead(uid))
                continue;

            if (TryComp(uid, out CFCombatantComponent? combat) && combat.CurrentHp < combat.MaxHp)
                _effects.Heal(uid, uid, healing.PerPulse);
        }
    }

    [SubscribeLocalEvent]
    private void OnDamaged(Entity<CFFastHealingComponent> ent, ref CFDamagedEvent args)
    {
        if (args.DamageType is not { } type || ent.Comp.DeactivatedBy.Count == 0)
            return;

        foreach (var stops in ent.Comp.DeactivatedBy)
        {
            if (!CFDamageTypes.Same(_prototypes, stops, type))
                continue;

            ent.Comp.StoppedUntil = _timing.CurTime + CFEidolonSystem.Pulse;
            return;
        }
    }
}

/// <summary>
/// Conditions a creature has by nature and never loses: a zombie's slowed 1. Only the conditions kept by value work
/// here (<c>CFConditionComponent.Values</c>): slowed, enfeebled, fatigued and the like.
/// </summary>
[RegisterComponent]
public sealed partial class CFInnateConditionsComponent : Component
{
    [DataField(required: true)]
    public Dictionary<CFConditionKind, int> Conditions = new();
}

public sealed partial class CFInnateConditionSystem : EntitySystem
{
    [Dependency] private CFConditionSystem _conditions = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFInnateConditionsComponent> ent, ref MapInitEvent args)
    {
        foreach (var (kind, value) in ent.Comp.Conditions)
        {
            _conditions.SetMarker(ent, kind, value);
            if (kind is not (CFConditionKind.Slowed or CFConditionKind.Quickened))
                continue;

            // The pool is made as the creature appears, which may be after this runs.
            EnsureComp<Content.Shared.CF.Actions.CFActionPoolComponent>(ent);
            _conditions.RefreshMaxActions(ent);
        }
    }
}
