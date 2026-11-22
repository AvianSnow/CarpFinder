using Content.Server.CF.GOAP.Nodes;
using Content.Shared.CF.Detection;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Items;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Goes to an investigation node and searches there.</summary>
public sealed partial class CFGOAPCheckNodeStepSystem : CFGOAPStepSystem
{
    private const float ClosetReach = 1.5f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.CheckNode;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!Goap.TryPickNode(ent, out var node))
            return;

        ent.Comp.Node = node;
        Steering.MoveTo(ent, _transform.GetWorldPosition(node));
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Node is not { } node || TerminatingOrDeleted(node))
            return true;

        var at = _transform.GetMapCoordinates(node);
        if ((at.Position - _transform.GetMapCoordinates(ent).Position).Length() > CFGOAPSystem.LkpArrival)
        {
            Steering.Travel(ent, at.Position);
            return false;
        }

        Steering.Stop(ent.Owner);
        Search(ent, node, at);
        return true;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        ent.Comp.Node = null;
    }

    /// <summary>Opens what is here if it is a closet, then Seeks. Hiders are only found by this, never known.</summary>
    private void Search(Entity<CFGOAPComponent> ent, EntityUid node, MapCoordinates at)
    {
        if (TryComp(node, out CFGOAPInvestigationNodeComponent? data) && (data.Tags & CFGOAPNodeTag.Closet) != 0)
        {
            foreach (var closet in _lookup.GetEntitiesInRange<EntityStorageComponent>(at, ClosetReach))
            {
                _storage.OpenStorage((closet.Owner, closet.Comp), ent);
            }
        }

        _stealth.TrySeek(ent, at);
        ent.Comp.Checked[node] = _timing.CurTime;
        ent.Comp.NodesChecked++;
    }
}
