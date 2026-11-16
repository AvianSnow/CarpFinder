using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Cooking;

/// <summary>Grinding at a mortar: use what it grinds on the mortar, wait, and hold the result.</summary>
public sealed partial class CFMortarSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private CFCookingSystem _cooking = default!;

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<CFMortarComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryResult(ent, args.Used, out _))
            return;

        args.Handled = true;
        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.GrindTime, new CFGrindDoAfterEvent(), ent, ent, args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
        _audio.PlayPredicted(ent.Comp.Sound, ent, args.User);
    }

    [SubscribeLocalEvent]
    private void OnGrindDone(Entity<CFMortarComponent> ent, ref CFGrindDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } used || !TryResult(ent, used, out var result))
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        _cooking.Replace(used, result);
        _popup.PopupEntity(Loc.GetString("cf-cooking-ground", ("item", used)), args.User, args.User);
    }

    /// <summary>What grinding this item in the mortar gives, if the mortar grinds it at all.</summary>
    public bool TryResult(Entity<CFMortarComponent> ent, EntityUid item, out EntProtoId result)
    {
        foreach (var (tag, gives) in ent.Comp.Grinds)
        {
            if (!_tags.HasTag(item, tag))
                continue;

            result = gives;
            return true;
        }

        result = default;
        return false;
    }
}
