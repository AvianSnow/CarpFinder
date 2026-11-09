using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Spellshapes. One changes the next spell, and is lost if the creature spends an action on anything else first, or if
/// a round goes by, the real-time stand-in for the end of its turn. <see cref="CFEffectSystem"/> asks here for the extra range
/// and area, and <see cref="CFSpellSystem"/> uses the spellshape up once the spell is cast.
/// </summary>
public sealed partial class CFSpellshapeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFActiveSpellshapeComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (now >= active.Expires)
            {
                _popup.PopupEntity(Loc.GetString("cf-spellshape-lost"), uid, uid);
                End(uid, active);
            }
        }
    }

    public int ExtraRangeFeet(EntityUid source, EntityUid user)
    {
        return HasComp<CFSpellComponent>(source) ? Current(user)?.ExtraRangeFeet ?? 0 : 0;
    }

    /// <summary>How many feet a spell's area grows. Only bursts of at least 10 feet, cones and lines qualify.</summary>
    public int ExtraAreaFeet(EntityUid source, EntityUid user, CFAreaEffectComponent area)
    {
        if (Current(user) is not { Widens: true } || !HasNoDuration(source))
            return 0;

        return area.Shape switch
        {
            CFAreaShape.Burst when area.SizeFeet >= 10 => 5,
            CFAreaShape.Cone or CFAreaShape.Line => area.SizeFeet <= 15 ? 5 : 10,
            _ => 0,
        };
    }

    public bool IsAmping(EntityUid user)
    {
        return Current(user) is { Amp: true };
    }

    public bool IsSubtle(EntityUid user)
    {
        return Current(user) is { Subtle: true };
    }

    /// <summary>The spell has been cast, so the spellshape is used up.</summary>
    public void Consume(EntityUid user)
    {
        if (!TryComp(user, out CFActiveSpellshapeComponent? active))
            return;

        // At once, not deferred: the spell's own cost is charged straight after this, and must not read as another action.
        _features.SetActionToggled(user, active.Shape, false);
        RemComp(user, active);
    }

    private CFSpellshapeComponent? Current(EntityUid user)
    {
        if (!TryComp(user, out CFActiveSpellshapeComponent? active) || !_prototypes.TryIndex(active.Shape, out var proto))
            return null;

        return proto.TryComp<CFSpellshapeComponent>(out var shape, Factory) ? shape : null;
    }

    public bool HasNoDuration(EntityUid source)
    {
        if (!TryComp(source, out CFSpellComponent? spell) || !_prototypes.TryIndex(spell.Spell, out var proto))
            return false;

        return string.IsNullOrWhiteSpace(proto.Duration)
               || proto.Duration.Contains("instant", StringComparison.OrdinalIgnoreCase);
    }

    private void End(EntityUid user, CFActiveSpellshapeComponent active)
    {
        _features.SetActionToggled(user, active.Shape, false);
        RemCompDeferred(user, active);
    }

    [SubscribeLocalEvent]
    private void OnPerform(Entity<CFSpellshapeComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || MetaData(ent).EntityPrototype is not { } proto)
            return;

        var user = args.Performer;
        var active = EnsureComp<CFActiveSpellshapeComponent>(user);
        if (active.Shape != default && active.Shape != proto.ID)
            _features.SetActionToggled(user, active.Shape, false);

        active.Shape = proto.ID;
        active.Expires = _timing.CurTime + CFBonusSystem.Round;
        active.SetTick = _timing.CurTick;
        Dirty(user, active);
        _features.SetActionToggled(user, proto.ID, true);
        _popup.PopupEntity(Loc.GetString("cf-spellshape-ready", ("name", MetaData(ent).EntityName)), user, user);
    }

    [SubscribeLocalEvent]
    private void OnActionsSpent(Entity<CFActiveSpellshapeComponent> ent, ref CFActionsSpentEvent args)
    {
        if (!_net.IsServer || ent.Comp.SetTick == _timing.CurTick)
            return;

        _popup.PopupEntity(Loc.GetString("cf-spellshape-lost"), ent, ent);
        End(ent, ent.Comp);
    }
}
