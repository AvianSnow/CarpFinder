using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>An Animist's attuned apparitions. The first vessel is the primary apparition's. An empty id is an apparition with no vessel yet.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFApparitionsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<string> Vessels = new();
}

/// <summary>An action that makes another attuned apparition the primary one (Circle of Spirits).</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFCircleOfSpiritsActionComponent : Component
{
}

/// <summary>A body lent by a vessel spell: a natural attack that lasts until the spell lapses. Holds what to put back.</summary>
[RegisterComponent]
public sealed partial class CFFormAttackComponent : Component
{
    [DataField]
    public TimeSpan Expires;

    [DataField]
    public int OldDice;

    [DataField]
    public int OldDie;

    [DataField]
    public string OldType = string.Empty;

    [DataField]
    public bool OldFinesse;

    [DataField]
    public bool OldAgile;
}

/// <summary>Gives the target a natural attack for a while, such as Devouring Dark Form's tentacle.</summary>
public sealed partial class CFFormAttackEffect : CFEffect
{
    [DataField]
    public int Dice = 1;

    [DataField]
    public int Die = 6;

    [DataField(required: true)]
    public string DamageType = string.Empty;

    [DataField]
    public bool Finesse;

    [DataField]
    public bool Agile;

    [DataField]
    public float DurationSeconds = 12f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFApparitionSystem>().StartForm(target, this);
    }
}

/// <summary>Gives back the target's reaction (Store Time).</summary>
public sealed partial class CFRestoreReactionEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || !entities.TryGetComponent(target, out CFCombatOptionsComponent? options))
            return;

        options.ReactionSpent = 0;
        entities.Dirty(target, options);
    }
}

/// <summary>The rules for apparition spell slots, which are separate from an Animist's prepared ones.</summary>
public static class CFApparitions
{
    /// <summary>
    /// True when casting this spell spends an apparition slot. A spell the Animist also has a prepared charge of uses the charge first.
    /// </summary>
    public static bool UsesSlot(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int rank)
    {
        return rank > 0
            && book.ApparitionSpells.Contains(spell)
            && !(book.Prepared && CFPreparedSpells.Charges(book, spell).Ready > 0);
    }

    public static int Total(CFSpellbookComponent book, int rank)
    {
        return rank >= 0 && rank < book.ApparitionSlots.Count ? book.ApparitionSlots[rank] : 0;
    }

    public static int Left(CFSpellbookComponent book, int rank)
    {
        var spent = rank >= 0 && rank < book.ApparitionExpended.Count ? book.ApparitionExpended[rank] : 0;
        return Math.Max(0, Total(book, rank) - spent);
    }

    public static void Spend(CFSpellbookComponent book, int rank)
    {
        while (book.ApparitionExpended.Count <= rank)
        {
            book.ApparitionExpended.Add(0);
        }

        book.ApparitionExpended[rank]++;
    }

    public static void AddSlots(CFSpellbookComponent book, int rank, int count)
    {
        while (book.ApparitionSlots.Count <= rank)
        {
            book.ApparitionSlots.Add(0);
        }

        book.ApparitionSlots[rank] += count;
    }
}

public enum CFSustainState : byte
{
    /// <summary>Not running, so casting starts it and pays for it.</summary>
    None,

    /// <summary>Already cast or sustained this round.</summary>
    TooSoon,

    /// <summary>Casting again sustains it, with no Focus Point.</summary>
    Ready,
}

/// <summary>
/// A sustained spell. Using its action again sustains it, for the action's cost and no Focus Point. That can happen once per round (6 seconds)
/// and has to happen by the end of the next one (12 seconds), for up to a minute.
/// </summary>
public static class CFSustain
{
    public static readonly TimeSpan SameRound = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan Lapses = TimeSpan.FromSeconds(12);
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(60);

    public static CFSustainState State(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, TimeSpan now)
    {
        var index = book.Sustaining.FindIndex(entry => entry.Spell == spell);
        if (index < 0)
            return CFSustainState.None;

        var entry = book.Sustaining[index];
        if (now - entry.Last > Lapses || now - entry.Started >= Longest)
            return CFSustainState.None;

        return now - entry.Last < SameRound ? CFSustainState.TooSoon : CFSustainState.Ready;
    }

    /// <summary>Starts the spell, replacing a run of it that has finished.</summary>
    public static void Start(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, TimeSpan now)
    {
        book.Sustaining.RemoveAll(entry => entry.Spell == spell);
        book.Sustaining.Add(new CFSustainedSpell(spell, now, now));
    }

    public static void Renew(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, TimeSpan now)
    {
        var index = book.Sustaining.FindIndex(entry => entry.Spell == spell);
        if (index >= 0)
            book.Sustaining[index] = new CFSustainedSpell(spell, book.Sustaining[index].Started, now);
    }
}

/// <summary>Attuning to apparitions, changing the primary one, and the bodies vessel spells lend.</summary>
public sealed partial class CFApparitionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;

    /// <summary>
    /// Adds an apparition's spells to the repertoire. The primary apparition's vessel spell is granted now, and a Focus Point with it.
    /// Any other apparition's waits until it becomes the primary one.
    /// </summary>
    public void Attune(EntityUid mob, List<ProtoId<CFSpellPrototype>> spells, ProtoId<CFSpellPrototype>? vessel, bool primary)
    {
        var book = EnsureComp<CFSpellbookComponent>(mob);
        foreach (var spell in spells)
        {
            if (!book.ApparitionSpells.Contains(spell))
                book.ApparitionSpells.Add(spell);

            _features.GrantSpell(mob, spell);
        }

        Dirty(mob, book);

        var apparitions = EnsureComp<CFApparitionsComponent>(mob);
        var id = vessel?.Id ?? string.Empty;
        if (primary)
        {
            apparitions.Vessels.Insert(0, id);
            if (vessel is { } primaryVessel)
                _features.GrantFocusSpell(mob, primaryVessel);
        }
        else
        {
            apparitions.Vessels.Add(id);
        }

        Dirty(mob, apparitions);
    }

    public void AddSlots(EntityUid mob, int rank, int count)
    {
        var book = EnsureComp<CFSpellbookComponent>(mob);
        CFApparitions.AddSlots(book, rank, count);
        Dirty(mob, book);
    }

    /// <summary>Makes the next attuned apparition the primary one, swapping which vessel spell the character has.</summary>
    public bool ChangePrimary(EntityUid mob)
    {
        if (!TryComp(mob, out CFApparitionsComponent? apparitions) || apparitions.Vessels.Count < 2)
            return false;

        var old = apparitions.Vessels[0];
        var next = apparitions.Vessels[1];
        apparitions.Vessels[0] = next;
        apparitions.Vessels[1] = old;
        Dirty(mob, apparitions);

        if (old.Length > 0)
            RemoveSpell(mob, "CF" + old);

        if (next.Length > 0)
            _features.GrantSpell(mob, next);

        return true;
    }

    private void RemoveSpell(EntityUid mob, string action)
    {
        EntityUid? found = null;
        foreach (var existing in _actions.GetActions(mob))
        {
            if (MetaData(existing).EntityPrototype?.ID == action)
                found = existing;
        }

        if (found != null)
            _actions.RemoveAction(mob, found);
    }

    [SubscribeLocalEvent]
    private void OnCircle(Entity<CFCircleOfSpiritsActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        // Not handled means the action costs nothing, which is right for one that could not be used.
        if (!TryComp(args.Performer, out CFApparitionsComponent? apparitions) || apparitions.Vessels.Count < 2)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-apparition-only-one"), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (!_net.IsServer)
            return;

        ChangePrimary(args.Performer);
        _popup.PopupEntity(Loc.GetString("cf-apparition-changed"), args.Performer, args.Performer);
    }

    public void StartForm(EntityUid target, CFFormAttackEffect form)
    {
        if (!_net.IsServer || !TryComp(target, out CFCombatOptionsComponent? options))
            return;

        // A second cast only renews the time, or the "old" attack would become the form's own.
        if (!TryComp(target, out CFFormAttackComponent? held))
        {
            held = AddComp<CFFormAttackComponent>(target);
            held.OldDice = options.NaturalDice;
            held.OldDie = options.NaturalDie;
            held.OldType = options.NaturalType;
            held.OldFinesse = options.NaturalFinesse;
            held.OldAgile = options.NaturalAgile;
        }

        held.Expires = _timing.CurTime + TimeSpan.FromSeconds(form.DurationSeconds);
        options.NaturalDice = form.Dice;
        options.NaturalDie = form.Die;
        options.NaturalType = form.DamageType;
        options.NaturalFinesse = form.Finesse;
        options.NaturalAgile = form.Agile;
        Dirty(target, options);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFFormAttackComponent, CFCombatOptionsComponent>();
        while (query.MoveNext(out var uid, out var held, out var options))
        {
            if (now < held.Expires)
                continue;

            options.NaturalDice = held.OldDice;
            options.NaturalDie = held.OldDie;
            options.NaturalType = held.OldType;
            options.NaturalFinesse = held.OldFinesse;
            options.NaturalAgile = held.OldAgile;
            Dirty(uid, options);
            RemCompDeferred<CFFormAttackComponent>(uid);
        }
    }
}
