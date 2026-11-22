namespace Content.Shared.CF.Shop;

/// <summary>Shop price in copper. 100 copper is 1 gp. Read from the entity prototype.</summary>
[RegisterComponent]
public sealed partial class CFItemPriceComponent : Component
{
    [DataField]
    public int Copper;

    /// <summary>
    /// Treasure: gems, art objects, raw materials and monster parts. The dungeon wizard's cleanup never takes it.
    /// Who buys it still pays their usual share of its worth.
    /// </summary>
    [DataField]
    public bool FullResale;
}
