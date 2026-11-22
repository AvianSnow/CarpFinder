using Content.Server.CF.GOAP.Influence;
using Content.Server.CF.GOAP.Nodes;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Items;

namespace Content.Server.CF.GOAP;

/// <summary>Which investigation node to check next.</summary>
public sealed partial class CFGOAPSystem
{
    private const float NodeTiles = 12f;

    /// <summary>
    /// A weighted random node around the last known position, so it is not predictable. Reachable means in sight of
    /// the creature or linked from the node it just checked. Pack mates each take a different one.
    /// </summary>
    public bool TryPickNode(Entity<CFGOAPComponent> ent, out EntityUid node)
    {
        node = default;
        if (ent.Comp.Lkp is not { } lkp)
            return false;

        var profile = Profile(ent);
        var now = _timing.CurTime;
        var total = 0f;
        var scored = new List<(EntityUid Node, float Score)>();
        foreach (var candidate in _lookup.GetEntitiesInRange<CFGOAPInvestigationNodeComponent>(lkp, NodeTiles))
        {
            if (IsTakenByPack(ent, candidate))
                continue;

            // A coward watches from a distance, and that includes not searching up close.
            var at = _transform.GetMapCoordinates(candidate);
            var position = at.Position;
            if ((position - lkp.Position).Length() < profile.LkpKeepOut || _steering.IsDangerous(at))
                continue;

            var since = ent.Comp.Checked.TryGetValue(candidate, out var when)
                ? (float) (now - when).TotalSeconds
                : float.MaxValue;
            var score = CFGOAPNodeScore.Score(
                candidate.Comp.Weight,
                position - lkp.Position,
                ent.Comp.LkpHeading,
                HasScent(ent, candidate),
                since,
                TagMultiplier(profile, candidate.Comp.Tags));

            if (score <= 0f)
                continue;

            scored.Add((candidate, score));
            total += score;
        }

        if (scored.Count == 0)
            return false;

        var pick = _random.NextFloat(0f, total);
        foreach (var (candidate, score) in scored)
        {
            node = candidate;
            pick -= score;
            if (pick <= 0f)
                break;
        }

        return true;
    }

    private bool HasScent(Entity<CFGOAPComponent> ent, EntityUid node)
    {
        if (Transform(node).GridUid is not { } grid)
            return false;

        var cell = _influence.CellAt(grid, CFGOAPInfluenceLayer.Scent, Transform(node).Coordinates);
        return cell.Intensity > 0f && (ent.Comp.Target == null || cell.Emitter == ent.Comp.Target);
    }

    private static float TagMultiplier(CFGOAPProfilePrototype profile, CFGOAPNodeTag tags)
    {
        var multiplier = 1f;
        foreach (var (tag, value) in profile.NodeTags)
        {
            if ((tags & tag) != 0)
                multiplier *= value;
        }

        return multiplier;
    }

    private bool IsTakenByPack(Entity<CFGOAPComponent> ent, EntityUid node)
    {
        var query = EntityQueryEnumerator<CFGOAPComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (uid != ent.Owner && other.Node == node && !_factions.CanHarm(ent, uid))
                return true;
        }

        return false;
    }
}
