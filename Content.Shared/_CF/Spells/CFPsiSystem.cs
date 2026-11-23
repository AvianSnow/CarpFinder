using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>Which amp a psi cantrip is cast with: its own, or one a feat gives in place of it.</summary>
public enum CFAmpVariant : byte
{
    Normal,

    /// <summary>Mental Balm: +2 status to Will saves against emotion effects for an ally, and its fear or stupor ends.</summary>
    MentalBalm,

    /// <summary>Homing Beacon: a creature the spell lands on cannot stay concealed or hidden for a minute.</summary>
    HomingBeacon,
}

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

    /// <summary>Which amp this cast was given. A feat's amp replaces the cantrip's own.</summary>
    public CFAmpVariant Variant;
}

/// <summary>On a creature whose Psyche is Unleashed.</summary>
[RegisterComponent]
public sealed partial class CFUnleashedComponent : Component
{
    [DataField]
    public TimeSpan Until;

    /// <summary>When the Psyche was Unleashed, for what has to follow it at once (Violent Unleash).</summary>
    [DataField]
    public TimeSpan Since;
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
    /// <summary>It has to come within this many seconds of Unleashing, as Violent Unleash is the next action. 0 is any time.</summary>
    [DataField]
    public float WithinSeconds;
}

/// <summary>
/// What a Psychic's class and feats set: the Oscillating Wave's energy, and Psychic Rapport. Set by <c>CFGrantPsychicOption</c>.
/// </summary>
[RegisterComponent]
public sealed partial class CFPsychicComponent : Component
{
    /// <summary>Conservation of Energy: the mind's spells deal fire and cold in turn.</summary>
    [DataField]
    public bool Oscillation;

    /// <summary>The energy the next oscillating spell has to be. Null when the caster is free to choose, so the spell keeps its own.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? NextEnergy;

    /// <summary>When the last oscillating spell was cast. A minute without one settles the mind back to neutral.</summary>
    [DataField]
    public TimeSpan LastOscillated;

    /// <summary>Psychic Rapport: +1 circumstance to Deception, Diplomacy and Intimidation after a mental spell.</summary>
    [DataField]
    public bool Rapport;
}

/// <summary>On a spell action as it is cast by the Oscillating Wave: the energy this cast deals.</summary>
[RegisterComponent]
public sealed partial class CFOscillatedComponent : Component
{
    [DataField]
    public ProtoId<CFDamageTypePrototype> Energy;

    [DataField]
    public TimeSpan Until;
}

/// <summary>What a Forbidden Thought forbids.</summary>
public enum CFForbiddenAction : byte
{
    Strike = 1,
    CastSpell = 2,
}

/// <summary>A psychic lock on a creature: the first time it tries the forbidden action, it takes mental damage with a basic Will save.</summary>
[RegisterComponent]
public sealed partial class CFForbiddenThoughtComponent : Component
{
    [DataField]
    public EntityUid Caster;

    [DataField]
    public CFForbiddenAction Forbidden = CFForbiddenAction.Strike;

    [DataField]
    public int Dice = 2;

    [DataField]
    public int Dc;

    /// <summary>The amped lock also stuns on a failed save.</summary>
    [DataField]
    public bool Stuns;

    [DataField]
    public TimeSpan Until;
}

/// <summary>Homing Beacon's mark: the creature cannot become concealed or hidden until this passes.</summary>
[RegisterComponent]
public sealed partial class CFBeaconedComponent : Component
{
    [DataField]
    public TimeSpan Until;
}

/// <summary>Recall the Teachings: an Occultism check that Aids every ally within 30 feet at once.</summary>
[RegisterComponent]
public sealed partial class CFRecallTeachingsActionComponent : Component
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
/// harder for 12 seconds and leaves the mind stupefied after. A round here is 6 seconds, as everywhere else. A feat's amp takes
/// the place of the cantrip's own. The Oscillating Wave's spells deal fire and cold in turn, and a Forbidden Thought waits on
/// its creature until it Strikes or casts.
/// </summary>
public sealed partial class CFPsiSystem : EntitySystem
{
    private static readonly TimeSpan AmpLasts = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Round = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan UnleashLasts = Round * 2;
    private static readonly TimeSpan OscillationSettles = TimeSpan.FromSeconds(60);
    private const float BacklashSeconds = 12f;
    private const int AidDc = 15;
    private const float RecallFeet = 30f;

    public static readonly ProtoId<CFDamageTypePrototype> Fire = "Fire";
    public static readonly ProtoId<CFDamageTypePrototype> Cold = "Cold";

    /// <summary>The Oscillating Wave's spells: its two standard psi cantrips and its rank 1 granted spell.</summary>
    private static readonly HashSet<string> OscillatingSpells = new() { "Frostbite", "Ignition", "BreatheFire" };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;

    /// <summary>Call as a spell is cast, before it resolves. Remembers the cast, and spends a Focus Point if the spell is amped.</summary>
    public void NoteCast(EntityUid caster, EntityUid spell)
    {
        if (TryComp(caster, out CFCombatOptionsComponent? options))
            options.LastSpellCast = _timing.CurTime;

        CheckLock(caster, CFForbiddenAction.CastSpell);
        var psychic = CompOrNull<CFPsychicComponent>(caster);
        if (psychic != null && TryComp(spell, out CFSpellComponent? cast) && _prototypes.TryIndex(cast.Spell, out var proto))
        {
            if (psychic.Rapport && proto.Traits.Contains("Mental"))
                Rapport(caster);

            if (psychic.Oscillation && OscillatingSpells.Contains(proto.ID))
                Oscillate(caster, psychic, spell, proto);
        }

        if (!TryComp(spell, out CFPsiCantripComponent? psi))
            return;

        psi.AmpedUntil = TimeSpan.Zero;
        psi.Variant = CFAmpVariant.Normal;
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
        psi.Variant = _spellshapes.CurrentAmpVariant(caster);
    }

    /// <summary>What the cast from this source is amped with, or null if it is not amped with the cantrip's own amp.</summary>
    public CFPsiCantripComponent? Amped(EntityUid source)
    {
        return TryComp(source, out CFPsiCantripComponent? psi) && _timing.CurTime < psi.AmpedUntil && psi.Variant == CFAmpVariant.Normal ? psi : null;
    }

    /// <summary>The feat's amp this cast was given, or <see cref="CFAmpVariant.Normal"/> when it has none.</summary>
    public CFAmpVariant FeatAmp(EntityUid source)
    {
        return TryComp(source, out CFPsiCantripComponent? psi) && _timing.CurTime < psi.AmpedUntil ? psi.Variant : CFAmpVariant.Normal;
    }

    /// <summary>Runs a feat's amp on a creature the cast reached. The effect pipeline calls it after the cantrip's own effects.</summary>
    public void ApplyFeatAmp(EntityUid source, EntityUid user, EntityUid target, bool landed)
    {
        switch (FeatAmp(source))
        {
            case CFAmpVariant.MentalBalm:
                // Only on the caster or an ally: the amp is for a cantrip cast on friends.
                if (_factions.CanHarm(user, target))
                    return;

                _bonuses.AddBonus(target, CFBonusStat.Will, CFBonusType.Status, 2, 60f, "Emotion");
                // The counteract is simplified: the fear or stupor ends.
                _conditions.EndCondition(target, CFConditionKind.Frightened);
                _conditions.EndCondition(target, CFConditionKind.Stupefied);
                _popup.PopupEntity(Loc.GetString("cf-psi-mental-balm"), target);
                break;
            case CFAmpVariant.HomingBeacon:
                if (!landed || target == user || !_factions.CanHarm(user, target))
                    return;

                Beacon(target);
                break;
        }
    }

    /// <summary>Homing Beacon: an invisible creature is only concealed, a concealed one is seen, and neither can hide again for a minute.</summary>
    public void Beacon(EntityUid target)
    {
        var invisible = _conditions.Has(target, CFConditionKind.Invisible);
        _conditions.EndCondition(target, CFConditionKind.Invisible);
        _conditions.EndCondition(target, CFConditionKind.Concealed);
        EntityManager.System<Content.Shared.CF.Detection.CFStealthSystem>().Reveal(target);
        EnsureComp<CFBeaconedComponent>(target).Until = _timing.CurTime + TimeSpan.FromSeconds(60);
        if (invisible)
            _conditions.ApplyConcealed(target, 5, breaks: false, 60f, beaconed: true);

        _popup.PopupEntity(Loc.GetString("cf-psi-homing-beacon"), target);
    }

    public bool IsBeaconed(EntityUid target)
    {
        return TryComp(target, out CFBeaconedComponent? beacon) && _timing.CurTime < beacon.Until;
    }

    /// <summary>Psychic Rapport: a mental spell opens the mind to others for a moment.</summary>
    private void Rapport(EntityUid caster)
    {
        foreach (var skill in new[] { "Deception", "Diplomacy", "Intimidation" })
        {
            _bonuses.AddBonus(caster, CFBonusStat.SkillChecks, CFBonusType.Circumstance, 1, 6f, skill: skill);
        }
    }

    /// <summary>
    /// Conservation of Energy: the first oscillating spell keeps its own energy, and each one after deals the other. A minute
    /// without one, or a long rest, settles the mind back to neutral.
    /// </summary>
    private void Oscillate(EntityUid caster, CFPsychicComponent psychic, EntityUid spell, CFSpellPrototype proto)
    {
        var now = _timing.CurTime;
        if (now - psychic.LastOscillated > OscillationSettles)
            psychic.NextEnergy = null;

        var energy = psychic.NextEnergy ?? (proto.Traits.Contains("Cold") ? Cold : Fire);
        var oscillated = EnsureComp<CFOscillatedComponent>(spell);
        oscillated.Energy = energy;
        oscillated.Until = now + AmpLasts;
        psychic.NextEnergy = energy == Fire ? Cold : Fire;
        psychic.LastOscillated = now;
        _popup.PopupEntity(Loc.GetString(energy == Fire ? "cf-psi-oscillate-fire" : "cf-psi-oscillate-cold"), caster, caster);
    }

    /// <summary>The energy this cast deals instead of its own, for the Oscillating Wave, or null.</summary>
    public ProtoId<CFDamageTypePrototype>? EnergyOverride(EntityUid source)
    {
        return TryComp(source, out CFOscillatedComponent? oscillated) && _timing.CurTime < oscillated.Until ? oscillated.Energy : (ProtoId<CFDamageTypePrototype>?) null;
    }

    /// <summary>The energy the caster's next oscillating spell deals, or null if the caster is free to choose.</summary>
    public ProtoId<CFDamageTypePrototype>? NextEnergy(EntityUid caster)
    {
        return CompOrNull<CFPsychicComponent>(caster)?.NextEnergy;
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

    // Forbidden Thought

    /// <summary>Places the lock. A second lock replaces the first.</summary>
    public void Lock(EntityUid caster, EntityUid target, CFForbiddenAction forbidden, int dice, int dc, bool stuns)
    {
        var lock_ = EnsureComp<CFForbiddenThoughtComponent>(target);
        lock_.Caster = caster;
        lock_.Forbidden = forbidden;
        lock_.Dice = dice;
        lock_.Dc = dc;
        lock_.Stuns = stuns;
        lock_.Until = _timing.CurTime + Round;
        _popup.PopupEntity(Loc.GetString(forbidden == CFForbiddenAction.Strike ? "cf-psi-forbidden-strike" : "cf-psi-forbidden-cast"), target);
    }

    /// <summary>
    /// The creature attempts an action. If a Forbidden Thought forbids it, the lock goes off: mental damage with a basic Will save,
    /// and stunned 1 on a failure when the lock was amped. The action itself still happens.
    /// </summary>
    public void CheckLock(EntityUid creature, CFForbiddenAction action)
    {
        if (!_net.IsServer || !TryComp(creature, out CFForbiddenThoughtComponent? lock_) || lock_.Forbidden != action)
            return;

        RemComp(creature, lock_);
        if (_timing.CurTime >= lock_.Until)
            return;

        var traits = new HashSet<ProtoId<CFTraitPrototype>> { "Mental" };
        var degree = _conditions.RollDefense(creature, CFDefenseType.Will, lock_.Dc, out _, traits);
        var rolled = _effects.Roll(lock_.Dice, 6, 0);
        var amount = degree switch
        {
            CFDegree.CriticalSuccess => 0,
            CFDegree.Success => rolled / 2,
            CFDegree.Failure => rolled,
            _ => rolled * 2,
        };

        if (amount > 0)
            _effects.DealDamage(lock_.Caster, creature, amount, "Mental", critical: degree == CFDegree.CriticalFailure);

        if (lock_.Stuns && degree is CFDegree.Failure or CFDegree.CriticalFailure)
            _conditions.ApplyCondition(creature, CFConditionKind.Stunned, 1, 0f, lock_.Caster, lock_.Dc);

        _popup.PopupEntity(Loc.GetString("cf-psi-forbidden-triggered"), creature);
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

        var locks = EntityQueryEnumerator<CFForbiddenThoughtComponent>();
        while (locks.MoveNext(out var uid, out var lock_))
        {
            if (now >= lock_.Until)
                RemCompDeferred(uid, lock_);
        }

        var beacons = EntityQueryEnumerator<CFBeaconedComponent>();
        while (beacons.MoveNext(out var uid, out var beacon))
        {
            if (now >= beacon.Until)
                RemCompDeferred(uid, beacon);
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

        var unleashed = EnsureComp<CFUnleashedComponent>(args.Performer);
        unleashed.Until = _timing.CurTime + UnleashLasts;
        unleashed.Since = _timing.CurTime;
        options.NextUnleash = _timing.CurTime + UnleashLasts + UnleashLasts;
        _popup.PopupEntity(Loc.GetString("cf-psi-unleashed"), args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnPsycheAttempt(Entity<CFNeedsUnleashedComponent> ent, ref ActionAttemptEvent args)
    {
        if (TryComp(args.User, out CFUnleashedComponent? unleashed)
            && (ent.Comp.WithinSeconds <= 0f || _timing.CurTime - unleashed.Since <= TimeSpan.FromSeconds(ent.Comp.WithinSeconds)))
        {
            return;
        }

        args.Cancelled = true;
        args.Reason = Loc.GetString(ent.Comp.WithinSeconds > 0f ? "cf-psi-needs-just-unleashed" : "cf-psi-needs-unleashed");
    }

    /// <summary>
    /// Recall the Teachings: one Occultism check against the Aid DC, and every ally within 30 feet has that Aid for 6 seconds:
    /// +2 to attack rolls and skill checks on a critical success, +1 on a success, -1 on a critical failure. The rules let the
    /// Psychic Aid one ally as a reaction; here the lesson reaches them all at once.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRecallTeachings(Entity<CFRecallTeachingsActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var user = args.Performer;
        var degree = _conditions.RollSkillAgainst(user, "Occultism", CFAbility.Intelligence, AidDc, out _);
        var bonus = degree switch
        {
            CFDegree.CriticalSuccess => 2,
            CFDegree.Success => 1,
            CFDegree.CriticalFailure => -1,
            _ => 0,
        };

        if (bonus == 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-psi-teachings-nothing"), user, user);
            return;
        }

        foreach (var ally in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(user).Coordinates, RecallFeet / CFMelee.FeetPerTile))
        {
            if (ally.Owner == user || _factions.CanHarm(user, ally))
                continue;

            _bonuses.AddBonus(ally, CFBonusStat.AttackRoll, CFBonusType.Circumstance, bonus, 6f);
            _bonuses.AddBonus(ally, CFBonusStat.SkillChecks, CFBonusType.Circumstance, bonus, 6f);
        }

        _popup.PopupEntity(Loc.GetString(bonus > 0 ? "cf-psi-teachings" : "cf-psi-teachings-wrong"), user);
    }
}
