// Ported from Starlight (https://github.com/ss14Starlight/space-station-14), their Scent system, with permission.

using Content.Shared.Actions;

namespace Content.Shared.CF.Scent;

/// <summary>
/// The Sniff action of a <see cref="CFSmellerComponent"/>. The server lays the trail (<c>CFScentServerSystem</c>) and the
/// client draws it (<c>CFScentClientSystem</c>). An abstract base, so each side subscribes once.
/// </summary>
public abstract partial class CFSharedScentSystem : EntitySystem
{
    [Dependency] protected SharedActionsSystem Actions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFSmellerComponent, ComponentInit>(OnSmellerInit);
        SubscribeLocalEvent<CFSmellerComponent, ComponentShutdown>(OnSmellerShutdown);
        SubscribeLocalEvent<CFSmellerComponent, CFToggleSniffActionEvent>(OnToggleSniff);
    }

    protected virtual void OnSmellerInit(Entity<CFSmellerComponent> ent, ref ComponentInit args)
    {
        Actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
    }

    protected virtual void OnSmellerShutdown(Entity<CFSmellerComponent> ent, ref ComponentShutdown args)
    {
        Actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);
    }

    private void OnToggleSniff(Entity<CFSmellerComponent> ent, ref CFToggleSniffActionEvent args)
    {
        if (args.Handled)
            return;

        SetSniffing(ent, !ent.Comp.Sniffing);
        args.Handled = true;
    }

    /// <summary>Turns scent sight on or off. Free: smelling costs nothing in PF2e.</summary>
    public virtual void SetSniffing(Entity<CFSmellerComponent> ent, bool sniffing)
    {
        if (ent.Comp.Sniffing == sniffing)
            return;

        ent.Comp.Sniffing = sniffing;
        Actions.SetToggled(ent.Comp.ToggleActionEntity, sniffing);
        Dirty(ent);
    }
}

public sealed partial class CFToggleSniffActionEvent : InstantActionEvent;
