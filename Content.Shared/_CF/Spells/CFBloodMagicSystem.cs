using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>
/// A Sorcerer's blood magic: what happens beside the spell when it casts one of its bloodline's spells. Server-only.
/// The rules let the Sorcerer choose between helping itself and hindering a target. Here the spell decides: cast at an
/// enemy, <see cref="OnTarget"/> runs on the first enemy it reaches, and otherwise <see cref="OnSelf"/> runs on the caster.
/// </summary>
[RegisterComponent]
public sealed partial class CFBloodMagicComponent : Component
{
    /// <summary>The bloodline's spells: its sorcerous gifts and its bloodline spells. A cantrip never sets it off.</summary>
    [DataField]
    public HashSet<ProtoId<CFSpellPrototype>> Spells = new();

    [DataField]
    public List<CFEffect> OnSelf = new();

    /// <summary>What an enemy the spell reaches gets. Empty means the bloodline only helps the caster.</summary>
    [DataField]
    public List<CFEffect> OnTarget = new();

    /// <summary>When the spell was cast on an ally, <see cref="OnSelf"/> goes to that ally ("you or one target").</summary>
    [DataField]
    public bool SharesWithAlly;

    /// <summary>The spell being cast, until its blood magic has been given.</summary>
    [DataField]
    public EntityUid? Pending;

    [DataField]
    public int PendingRank;

    /// <summary>The ally the pending cast reached first, for <see cref="SharesWithAlly"/>.</summary>
    [DataField]
    public EntityUid? PendingAlly;

    /// <summary>When blood magic last took effect, and the rank of the spell that set it off (Bleed Out).</summary>
    [DataField]
    public TimeSpan LastAt;

    [DataField]
    public int LastRank;
}

/// <summary>Runs a Sorcerer's blood magic. <see cref="CFSpellSystem"/> tells it when a cast is paid for and when it is over.</summary>
public sealed partial class CFBloodMagicSystem : EntitySystem
{
    /// <summary>How long after blood magic an action that needs it can still be used.</summary>
    public static readonly TimeSpan Lingers = TimeSpan.FromSeconds(6);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;

    /// <summary>A cast has been paid for. A bloodline spell that is not a cantrip starts blood magic.</summary>
    public void Begin(EntityUid caster, Entity<CFSpellComponent> spell)
    {
        if (!_net.IsServer || !TryComp(caster, out CFBloodMagicComponent? blood))
            return;

        blood.Pending = null;
        blood.PendingAlly = null;
        if (!blood.Spells.Contains(spell.Comp.Spell) || spell.Comp.Rank == 0 && !spell.Comp.Focus)
            return;

        var level = CompOrNull<CFCombatantComponent>(caster)?.Level ?? 1;
        blood.Pending = spell;
        blood.PendingRank = spell.Comp.CastRank > 0 ? spell.Comp.CastRank : CFCasting.CantripRank(level);
    }

    /// <summary>The spell reached a creature. The first enemy gets the bloodline's effect, and that is the blood magic.</summary>
    public void NoteTarget(EntityUid caster, EntityUid source, EntityUid target)
    {
        if (!_net.IsServer
            || !TryComp(caster, out CFBloodMagicComponent? blood)
            || blood.Pending != source
            || target == caster)
        {
            return;
        }

        if (!_factions.CanHarm(caster, target))
        {
            blood.PendingAlly ??= target;
            return;
        }

        if (blood.OnTarget.Count == 0)
            return;

        Run(caster, blood, blood.OnTarget, target);
    }

    /// <summary>The cast is over. Blood magic that went to no enemy goes to the caster, or to the ally the spell was for.</summary>
    public void Finish(EntityUid caster)
    {
        if (!_net.IsServer || !TryComp(caster, out CFBloodMagicComponent? blood) || blood.Pending == null)
            return;

        var target = blood.SharesWithAlly && blood.PendingAlly is { } ally && !TerminatingOrDeleted(ally) ? ally : caster;
        Run(caster, blood, blood.OnSelf, target);
    }

    private void Run(EntityUid caster, CFBloodMagicComponent blood, List<CFEffect> effects, EntityUid target)
    {
        var rank = Math.Max(1, blood.PendingRank);
        blood.Pending = null;
        blood.PendingAlly = null;
        blood.LastAt = _timing.CurTime;
        blood.LastRank = rank;
        if (effects.Count == 0)
            return;

        // Effects written "per spell rank" use their heighten fields: rank 1 is no step.
        _effects.Run(effects, new CFEffectArgs
        {
            User = caster,
            Source = caster,
            Target = target,
            Position = Transform(target).Coordinates,
            Rank = rank,
            HeightenSteps = rank - 1,
        });
    }

    /// <summary>Whether blood magic took effect for this creature in the last 6 seconds.</summary>
    public bool Recent(EntityUid caster)
    {
        return TryComp(caster, out CFBloodMagicComponent? blood)
               && blood.LastRank > 0
               && _timing.CurTime - blood.LastAt <= Lingers;
    }
}

/// <summary>Bleed Out: persistent bleed equal to the rank of the spell whose blood magic the user last had.</summary>
public sealed partial class CFBleedOutEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || !entities.TryGetComponent(args.User, out CFBloodMagicComponent? blood))
            return;

        entities.System<CFBonusSystem>().AddPersistent(target, "Bleed", 0, 0, Math.Max(1, blood.LastRank), args.User);
    }
}

/// <summary>Jealous Hex: the target is enfeebled, clumsy, drained or stupefied, by which of its attributes is highest.</summary>
public sealed partial class CFJealousHexEffect : CFEffect
{
    [DataField]
    public int Value = 1;

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var kind = CFConditionKind.Stupefied;
        if (entities.TryGetComponent(target, out CFAbilityScoreComponent? abilities))
        {
            var best = int.MinValue;
            foreach (var (ability, condition) in new[]
                     {
                         (Content.Shared.CF.Preferences.CFAbility.Strength, CFConditionKind.Enfeebled),
                         (Content.Shared.CF.Preferences.CFAbility.Dexterity, CFConditionKind.Clumsy),
                         (Content.Shared.CF.Preferences.CFAbility.Constitution, CFConditionKind.Drained),
                         (Content.Shared.CF.Preferences.CFAbility.Intelligence, CFConditionKind.Stupefied),
                         (Content.Shared.CF.Preferences.CFAbility.Wisdom, CFConditionKind.Stupefied),
                         (Content.Shared.CF.Preferences.CFAbility.Charisma, CFConditionKind.Stupefied),
                     })
            {
                var modifier = abilities.GetModifier(ability);
                if (modifier <= best)
                    continue;

                best = modifier;
                kind = condition;
            }
        }

        entities.System<CFConditionSystem>().ApplyCondition(target, kind, Value, DurationSeconds, args.User, args.Dc);
    }
}
