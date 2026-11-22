using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>A weapon or shield that carries a deity's symbol, from Emblazon Armament.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFEmblazonedComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<CFDeityPrototype> Deity;

    /// <summary>Who drew it. Each character keeps one symbol at a time.</summary>
    [DataField]
    public EntityUid? Emblazoner;
}

/// <summary>On the Emblazon Armament action.</summary>
[RegisterComponent]
public sealed partial class CFEmblazonActionComponent : Component
{
}

/// <summary>
/// Emblazon Armament: a follower of the deity deals 1 more damage with the marked weapon, and a marked shield has 1 more
/// Hardness for them. Both are status bonuses. The rules take 10 minutes to draw the symbol. Here the action is instant and
/// then has a 10 minute cooldown.
/// </summary>
public sealed partial class CFEmblazonSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>The deity a creature follows, if any.</summary>
    public CFDeityPrototype? DeityOf(EntityUid uid)
    {
        return CompOrNull<CFExperienceComponent>(uid)?.Build is { } build
            ? CFClassChoices.SelectedDeity(_prototypes, build)
            : null;
    }

    /// <summary>1 when the item carries the symbol of the deity <paramref name="user"/> follows, otherwise 0.</summary>
    public int Bonus(EntityUid user, EntityUid item)
    {
        return TryComp(item, out CFEmblazonedComponent? mark) && DeityOf(user)?.ID == mark.Deity.Id ? 1 : 0;
    }

    /// <summary>Marks a weapon or shield, and takes the symbol off whatever this creature marked before.</summary>
    public bool TryEmblazon(EntityUid user, EntityUid item)
    {
        if (DeityOf(user) is not { } deity || !HasComp<CFWeaponComponent>(item) && !HasComp<CFShieldComponent>(item))
            return false;

        var query = AllEntityQuery<CFEmblazonedComponent>();
        while (query.MoveNext(out var old, out var mark))
        {
            if (old != item && mark.Emblazoner == user)
                RemCompDeferred<CFEmblazonedComponent>(old);
        }

        var comp = EnsureComp<CFEmblazonedComponent>(item);
        comp.Deity = deity.ID;
        comp.Emblazoner = user;
        Dirty(item, comp);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFEmblazonActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (DeityOf(args.User) != null
            && _hands.TryGetActiveItem(args.User, out var held)
            && (HasComp<CFWeaponComponent>(held) || HasComp<CFShieldComponent>(held)))
        {
            return;
        }

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-emblazon-cannot");
    }

    [SubscribeLocalEvent]
    private void OnEmblazon(Entity<CFEmblazonActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || !_hands.TryGetActiveItem(args.Performer, out var held) || !TryEmblazon(args.Performer, held.Value))
            return;

        _popup.PopupEntity(Loc.GetString("cf-emblazon-done", ("item", held.Value)), args.Performer, args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CFEmblazonedComponent> ent, ref ExaminedEvent args)
    {
        if (_prototypes.TryIndex(ent.Comp.Deity, out var deity))
            args.PushMarkup(Loc.GetString("cf-emblazon-examine", ("deity", Loc.GetString(deity.Name))));
    }
}
