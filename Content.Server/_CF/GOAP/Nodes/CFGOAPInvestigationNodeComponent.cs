using System.Numerics;
using Content.Shared.CF.Items;

namespace Content.Server.CF.GOAP.Nodes;

/// <summary>
/// A hidden hint that mappers place near corners, closets and doorways. An advanced NPC that has lost its target
/// checks these around the last known position. See <c>.agent/investigation-nodes.md</c>.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPInvestigationNodeComponent : Component
{
    [DataField]
    public float Weight = 1f;

    [DataField]
    public CFGOAPNodeTag Tags;

    /// <summary>Nodes worth checking next, as graph edges. A stairs node links to the one on the next level.</summary>
    [DataField]
    public List<EntityUid> Linked = new();
}

/// <summary>Node scoring as plain math.</summary>
public static class CFGOAPNodeScore
{
    /// <summary>The distance at which a node is worth half as much.</summary>
    private const float FalloffTiles = 6f;

    private const float ScentBonus = 2f;

    /// <summary>A node just checked is worth this much, recovering over <see cref="RecheckSeconds"/>.</summary>
    private const float JustCheckedShare = 0.05f;

    private const float RecheckSeconds = 60f;

    /// <summary>
    /// Base weight × distance falloff × match with the target's heading × scent bonus × "not checked recently".
    /// Heading only ever adds: a node behind the target is less likely, not ruled out.
    /// </summary>
    public static float Score(
        float weight,
        Vector2 fromLkp,
        Vector2 lastHeading,
        bool hasScent,
        float secondsSinceChecked,
        float tagMultiplier = 1f)
    {
        var distance = fromLkp.Length();
        var falloff = 1f / (1f + distance / FalloffTiles);

        var direction = 1f;
        if (distance > 0f && lastHeading != Vector2.Zero)
            direction = 1.5f + Vector2.Dot(fromLkp / distance, Vector2.Normalize(lastHeading));

        var recency = Math.Clamp(secondsSinceChecked / RecheckSeconds, JustCheckedShare, 1f);
        return weight * falloff * direction * (hasScent ? ScentBonus : 1f) * recency * tagMultiplier;
    }
}
