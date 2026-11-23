using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server.CF.Character;

/// <summary>
/// Collapse Construct: the construct innovation folds into a case the Inventor carries, and unfolds from it beside them with the
/// Hit Points it had. Folded, it cannot act or be hurt. It is spawned again from the build, so its modifications come back with it.
/// </summary>
public sealed partial class CFInventorServerSystem : EntitySystem
{
    private const string CasePrototype = "CFCollapsedConstruct";

    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private CFInventorSystem _inventor = default!;
    [Dependency] private CFCharacterSpawnSystem _spawn = default!;

    [SubscribeLocalEvent]
    private void OnCollapseAttempt(Entity<CFCollapseConstructActionComponent> ent, ref Content.Shared.Actions.Events.ActionAttemptEvent args)
    {
        if (_inventor.ConstructInReach(args.User) != null || FindCase(args.User) != null)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-inventor-no-construct");
    }

    [SubscribeLocalEvent]
    private void OnCollapse(Entity<CFCollapseConstructActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.Performer;
        if (_inventor.ConstructInReach(user) is { } construct)
        {
            Collapse(user, construct);
            return;
        }

        if (FindCase(user) is { } folded)
            Unfold(user, folded);
    }

    /// <summary>The folded construct the Inventor holds, or one beside them.</summary>
    public Entity<CFCollapsedConstructComponent>? FindCase(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp(held, out CFCollapsedConstructComponent? folded) && folded.Inventor == user)
                return (held, folded);
        }

        var here = _transform.GetMapCoordinates(user);
        var query = EntityQueryEnumerator<CFCollapsedConstructComponent>();
        while (query.MoveNext(out var uid, out var folded))
        {
            if (folded.Inventor == user && _transform.GetMapCoordinates(uid).InRange(here, CFInventorSystem.AdjacentTiles))
                return (uid, folded);
        }

        return null;
    }

    public void Collapse(EntityUid user, EntityUid construct)
    {
        if (!_mobState.IsAlive(construct) || !TryComp(construct, out CFCombatantComponent? body))
            return;

        var folded = Spawn(CasePrototype, Transform(user).Coordinates);
        var comp = EnsureComp<CFCollapsedConstructComponent>(folded);
        comp.Inventor = user;
        comp.CurrentHp = body.CurrentHp;
        _metaData.SetEntityName(folded, Loc.GetString("cf-inventor-case-name", ("construct", MetaData(construct).EntityName)));
        QueueDel(construct);
        _hands.TryPickupAnyHand(user, folded, animate: false);
        _popup.PopupEntity(Loc.GetString("cf-inventor-construct-collapsed"), user, user);
    }

    public void Unfold(EntityUid user, Entity<CFCollapsedConstructComponent> folded)
    {
        if (!TryComp(user, out CFExperienceComponent? xp) || xp.Build == null || !TryComp(user, out CFCombatantComponent? combat))
            return;

        _spawn.EnsureCompanion(user, xp.Build, combat.Level);
        if (_inventor.Construct(user) is not { } construct)
            return;

        if (TryComp(construct, out CFCombatantComponent? body))
            _inventor.SetHitPoints(construct, body, folded.Comp.CurrentHp);

        Del(folded);
        _popup.PopupEntity(Loc.GetString("cf-inventor-construct-unfolded"), user, user);
    }
}
