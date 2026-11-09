using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>A spell a Psychic can amp, and what the amp does. It sits on the spell's action. See <see cref="CFPsiSystem"/>.</summary>
[RegisterComponent]
public sealed partial class CFPsiCantripComponent : Component
{
    /// <summary>Replaces the first damage effect's number of dice. 0 keeps it.</summary>
    [DataField]
    public int AmpDice;

    /// <summary>Replaces the damage die. 0 keeps it.</summary>
    [DataField]
    public int AmpDie;

    /// <summary>Replaces the dice added per heighten step. 0 keeps it.</summary>
    [DataField]
    public int AmpHeightenDice;

    /// <summary>Replaces how long a resistance lasts. 0 keeps it.</summary>
    [DataField]
    public float AmpDurationSeconds;

    /// <summary>More targets, as Imaginary Weapon's second weapon.</summary>
    [DataField]
    public int AmpExtraTargets;

    /// <summary>The target is off-guard to the caster for this attack.</summary>
    [DataField]
    public bool AmpOffGuard;

    /// <summary>Runs on every target.</summary>
    [DataField]
    public List<CFEffect> AmpEffects = new();

    [DataField]
    public List<CFEffect> AmpCritical = new();

    [DataField]
    public List<CFEffect> AmpSuccess = new();

    [DataField]
    public List<CFEffect> AmpFailure = new();

    [DataField]
    public List<CFEffect> AmpCriticalFailure = new();

    /// <summary>Until this time, this cast is amped. A projectile's effects land after the cast has finished.</summary>
    public TimeSpan AmpedUntil;
}

/// <summary>On a creature whose Psyche is Unleashed.</summary>
[RegisterComponent]
public sealed partial class CFUnleashedComponent : Component
{
    [DataField]
    public TimeSpan Until;
}

/// <summary>An action that unleashes the user's Psyche.</summary>
[RegisterComponent]
public sealed partial class CFUnleashActionComponent : Component
{
}

/// <summary>A psyche action: it can only be used while the Psyche is Unleashed.</summary>
[RegisterComponent]
public sealed partial class CFNeedsUnleashedComponent : Component
{
}

/// <summary>Glimpse Weakness: the first ally to hit this creature adds precision damage.</summary>
[RegisterComponent]
public sealed partial class CFGlimpsedComponent : Component
{
    [DataField]
    public EntityUid Caster;

    [DataField]
    public int Bonus;

    [DataField]
    public TimeSpan Until;
}

/// <summary>Marks the target for Glimpse Weakness.</summary>
public sealed partial class CFGlimpseEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFPsiSystem>().Glimpse(args.User, target, Math.Max(1, args.Rank), entities.System<CFPsiSystem>().Amped(args.Source) != null);
    }
}

/// <summary>
/// Psychics. The Amp action arms the next psi cantrip, which spends a Focus Point as it is cast. Unleash Psyche makes spells hit
/// harder for 12 seconds and leaves the mind stupefied after. A round here is 6 seconds, as everywhere else.
/// </summary>
public sealed partial class CFPsiSystem : EntitySystem
{
    private static readonly TimeSpan AmpLasts = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Round = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan UnleashLasts = Round * 2;
    private const float BacklashSeconds = 12f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;

    /// <summary>Call as a spell is cast, before it resolves. Remembers the cast, and spends a Focus Point if the spell is amped.</summary>
    public void NoteCast(EntityUid caster, EntityUid spell)
    {
        if (TryComp(caster, out CFCombatOptionsComponent? options))
            options.LastSpellCast = _timing.CurTime;

        if (!TryComp(spell, out CFPsiCantripComponent? psi))
            return;

        psi.AmpedUntil = TimeSpan.Zero;
        if (!_spellshapes.IsAmping(caster))
            return;

        if (!TryComp(caster, out CFSpellbookComponent? book) || CFFocusSystem.Remaining(book) <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-no-focus"), caster, caster);
            return;
        }

        book.FocusSpent++;
        Dirty(caster, book);
        psi.AmpedUntil = _timing.CurTime + AmpLasts;
    }

    /// <summary>What the cast from this source is amped with, or null if it is not amped.</summary>
    public CFPsiCantripComponent? Amped(EntityUid source)
    {
        return TryComp(source, out CFPsiCantripComponent? psi) && _timing.CurTime < psi.AmpedUntil ? psi : null;
    }

    public bool CanUnleash(EntityUid user)
    {
        return TryComp(user, out CFCombatOptionsComponent? options)
               && !HasComp<CFUnleashedComponent>(user)
               && _timing.CurTime >= options.NextUnleash
               && options.LastSpellCast != TimeSpan.Zero
               && _timing.CurTime - options.LastSpellCast <= Round;
    }

    public void Glimpse(EntityUid caster, EntityUid target, int rank, bool amped)
    {
        var glimpsed = EnsureComp<CFGlimpsedComponent>(target);
        glimpsed.Caster = caster;
        glimpsed.Until = _timing.CurTime + TimeSpan.FromSeconds(60);
        glimpsed.Bonus = 1 + rank;
        if (!amped)
            return;

        // 1d4+1, and another 1d4+1 for every 2 ranks past the first.
        glimpsed.Bonus = 0;
        for (var i = 0; i < 1 + (rank - 1) / 2; i++)
        {
            glimpsed.Bonus += _random.Next(1, 5) + 1;
        }
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFUnleashedComponent>();
        while (query.MoveNext(out var uid, out var unleashed))
        {
            if (now < unleashed.Until)
                continue;

            RemCompDeferred(uid, unleashed);
            _conditions.ApplyCondition(uid, CFConditionKind.Stupefied, 1, BacklashSeconds, uid, 0);
        }
    }

    [SubscribeLocalEvent]
    private void OnUnleashAttempt(Entity<CFUnleashActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (CanUnleash(args.User))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-psi-cannot-unleash");
    }

    [SubscribeLocalEvent]
    private void OnUnleash(Entity<CFUnleashActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || !CanUnleash(args.Performer) || !TryComp(args.Performer, out CFCombatOptionsComponent? options))
            return;

        EnsureComp<CFUnleashedComponent>(args.Performer).Until = _timing.CurTime + UnleashLasts;
        options.NextUnleash = _timing.CurTime + UnleashLasts + UnleashLasts;
        _popup.PopupEntity(Loc.GetString("cf-psi-unleashed"), args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnPsycheAttempt(Entity<CFNeedsUnleashedComponent> ent, ref ActionAttemptEvent args)
    {
        if (HasComp<CFUnleashedComponent>(args.User))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-psi-needs-unleashed");
    }
}
