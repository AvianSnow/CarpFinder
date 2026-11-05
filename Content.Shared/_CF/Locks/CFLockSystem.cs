using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Locks;

/// <summary>
/// Keys, Pick a Lock (Thievery) and Force Open (Athletics). The stock <see cref="LockSystem"/> keeps the locked
/// state, and stock toggles are refused so that a key, a pick, or force is the only way past.
/// Based on CP14's <c>SharedCP14LockKeySystem</c>.
/// </summary>
public sealed partial class CFLockSystem : EntitySystem
{
    private const string Thievery = "Thievery";
    private const string Athletics = "Athletics";

    /// <summary>Pick a Lock is a two-action activity.</summary>
    public const int PickActions = 2;

    /// <summary>Force Open is a single action.</summary>
    public const int ForceActions = 1;

    /// <summary>Item penalty to Force Open without a crowbar.</summary>
    public const int NoCrowbarPenalty = -2;

    /// <summary>Circumstance penalty after jamming it with a critical failure.</summary>
    public const int JammedPenalty = -2;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;

    [SubscribeLocalEvent]
    private void OnLockToggleAttempt(Entity<CFLockComponent> ent, ref LockToggleAttemptEvent args)
    {
        args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnBeforeDoorOpened(EntityUid uid, CFLockComponent comp, BeforeDoorOpenedEvent args)
    {
        if (_lock.IsLocked(uid))
            args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<CFLockComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out CFKeyComponent? key))
            return;

        args.Handled = true;
        TryUseKey(ent, args.User, key);
    }

    /// <summary>Turns a key in the lock: unlocks it, or locks it again. A door or chest must be shut first.</summary>
    public bool TryUseKey(Entity<CFLockComponent> ent, EntityUid user, CFKeyComponent key)
    {
        if (!_net.IsServer)
            return false;

        if (ent.Comp.KeyId != key.KeyId)
        {
            _popup.PopupEntity(Loc.GetString("cf-lock-key-no-fit"), ent, user);
            return false;
        }

        if (ent.Comp.Broken)
        {
            _popup.PopupEntity(Loc.GetString("cf-lock-broken", ("lock", ent.Owner)), ent, user);
            return false;
        }

        if (_lock.IsLocked(ent.Owner))
        {
            Open(ent, user);
            return true;
        }

        if (!IsShut(ent))
        {
            _popup.PopupEntity(Loc.GetString("cf-lock-not-shut", ("lock", ent.Owner)), ent, user);
            return false;
        }

        _lock.Lock(ent, user);
        return true;
    }

    private bool IsShut(EntityUid uid)
    {
        if (TryComp(uid, out DoorComponent? door) && door.State != DoorState.Closed)
            return false;

        return !TryComp(uid, out EntityStorageComponent? storage) || !storage.Open;
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFLockComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_lock.IsLocked(ent.Owner))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("cf-lock-pick"),
            Message = Loc.GetString(
                "cf-lock-pick-message",
                ("dc", ent.Comp.Dc),
                ("successes", ent.Comp.Successes),
                ("needed", ent.Comp.SuccessesNeeded)),
            Act = () => TryPick(ent, user),
        });
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("cf-lock-force"),
            Message = Loc.GetString("cf-lock-force-message", ("dc", ent.Comp.ForceDc)),
            Act = () => TryForceOpen(ent, user),
        });
    }

    /// <summary>
    /// Pick a Lock: Thievery against the lock's DC. Needs training and a working toolkit.
    /// Successes add up until the lock opens. A critical failure breaks the toolkit.
    /// </summary>
    public bool TryPick(Entity<CFLockComponent> ent, EntityUid user)
    {
        if (!_net.IsServer || TerminatingOrDeleted(ent) || !_lock.IsLocked(ent.Owner))
            return false;

        if (!IsTrained(user, Thievery))
        {
            _popup.PopupEntity(Loc.GetString("cf-lock-pick-untrained"), ent, user);
            return false;
        }

        if (FindToolkit(user) is not { } toolkit)
        {
            _popup.PopupEntity(Loc.GetString("cf-lock-pick-no-toolkit"), ent, user);
            return false;
        }

        if (!_actions.TrySpendActions(user, PickActions))
            return false;

        var degree = _conditions.RollSkillAgainst(user, Thievery, CFAbility.Dexterity, ent.Comp.Dc, out var total);
        PopupRoll(user, Thievery, total, degree);

        // A critical success leaves no trace of itself, though earlier marks stay.
        ent.Comp.Tampered |= degree is CFDegree.Success or CFDegree.CriticalFailure;

        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                ent.Comp.Successes += 2;
                break;
            case CFDegree.Success:
                ent.Comp.Successes += 1;
                break;
            case CFDegree.CriticalFailure:
                toolkit.Comp.Broken = true;
                Dirty(toolkit);
                _popup.PopupEntity(Loc.GetString("cf-lock-toolkit-breaks"), ent, user, PopupType.MediumCaution);
                break;
        }

        Dirty(ent);

        if (ent.Comp.Successes >= ent.Comp.SuccessesNeeded)
        {
            Open(ent, user);
            return true;
        }

        if (degree.IsSuccess())
        {
            _popup.PopupEntity(
                Loc.GetString("cf-lock-progress", ("successes", ent.Comp.Successes), ("needed", ent.Comp.SuccessesNeeded)),
                ent,
                user);
        }

        return false;
    }

    /// <summary>
    /// Force Open: Athletics with the attack trait, so it pays and adds to the multiple attack penalty.
    /// A critical success opens it undamaged, a success breaks the lock, and a critical failure jams it.
    /// </summary>
    public bool TryForceOpen(Entity<CFLockComponent> ent, EntityUid user)
    {
        if (!_net.IsServer || TerminatingOrDeleted(ent) || !_lock.IsLocked(ent.Owner))
            return false;

        if (!_actions.TrySpendActions(user, ForceActions))
            return false;

        var modifier = CrowbarModifier(user) + _actions.MapPenalty(user, agile: false);
        if (ent.Comp.JammedBy.Contains(user))
            modifier += JammedPenalty;

        var degree = _conditions.RollSkillAgainst(
            user,
            Athletics,
            CFAbility.Strength,
            ent.Comp.ForceDc,
            out var total,
            attackTrait: true,
            itemBonus: modifier);
        _actions.CountAttack(user);
        PopupRoll(user, Athletics, total, degree);

        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                Open(ent, user);
                return true;
            case CFDegree.Success:
                ent.Comp.Broken = true;
                Open(ent, user);
                return true;
            case CFDegree.CriticalFailure:
                ent.Comp.JammedBy.Add(user);
                _popup.PopupEntity(Loc.GetString("cf-lock-jammed", ("lock", ent.Owner)), ent, user, PopupType.MediumCaution);
                break;
        }

        return false;
    }

    private void Open(Entity<CFLockComponent> ent, EntityUid user)
    {
        ent.Comp.Successes = 0;
        Dirty(ent);
        _lock.Unlock(ent, user);
    }

    private bool IsTrained(EntityUid user, string skill)
    {
        return TryComp(user, out CFCombatantComponent? combat)
               && CFDefense.SkillRank(_prototypes, combat, skill) >= CFProficiencyRank.Trained;
    }

    private Entity<CFThievesToolkitComponent>? FindToolkit(EntityUid user)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(user))
        {
            if (TryComp(item, out CFThievesToolkitComponent? toolkit) && !toolkit.Broken)
                return (item, toolkit);
        }

        return null;
    }

    private int CrowbarModifier(EntityUid user)
    {
        var modifier = NoCrowbarPenalty;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp(held, out CFCrowbarComponent? crowbar))
                modifier = Math.Max(modifier, crowbar.ItemBonus);
        }

        return modifier;
    }

    /// <summary>"Name: rolled Thievery 14 — Success" over the creature, for everyone who can see it.</summary>
    private void PopupRoll(EntityUid creature, string skill, int total, CFDegree degree)
    {
        var success = degree.IsSuccess();
        _popup.PopupEntity(
            Loc.GetString(
                success ? "cf-lock-roll-success" : "cf-lock-roll-failure",
                ("name", Identity.Entity(creature, EntityManager)),
                ("skill", skill),
                ("total", total)),
            creature,
            success ? PopupType.Medium : PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CFLockComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Broken)
            args.PushMarkup(Loc.GetString("cf-lock-examine-broken"));
        else if (ent.Comp.Tampered)
            args.PushMarkup(Loc.GetString("cf-lock-examine-tampered"));
    }

    [SubscribeLocalEvent]
    private void OnToolkitExamined(Entity<CFThievesToolkitComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Broken)
            args.PushMarkup(Loc.GetString("cf-lock-examine-toolkit-broken"));
    }
}
