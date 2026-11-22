using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Arrows a monster carries for its held bow, as a stat block's "shortbow (20 arrows)". A bow holds one arrow and
/// nothing in stock AI reloads it, so <see cref="CFArrowSupplySystem"/> nocks the next one as each shot leaves.
/// This works for any AI. Leave the arrow already on the bow out of <see cref="Arrows"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CFArrowSupplyComponent : Component
{
    [DataField]
    public int Arrows;

    [DataField]
    public EntProtoId Ammo = "CFArrow";
}
