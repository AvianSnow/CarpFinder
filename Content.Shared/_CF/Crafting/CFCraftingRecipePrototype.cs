using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Crafting;

/// <summary>
/// What a workbench can make, and from what. The Craft DC and level limits come from the result's <c>CFItem</c>.
/// Based on CP14's <c>CP14WorkbenchRecipePrototype</c>.
/// </summary>
[Prototype("craftingRecipe")]
public sealed partial class CFCraftingRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Workbenches listing this tag in <see cref="CFWorkbenchComponent.RecipeTags"/> can make it.</summary>
    [DataField(required: true)]
    public ProtoId<TagPrototype> Tag;

    /// <summary>What kind of thing it makes. A formula book teaches one or more categories, and the bench window groups by it.</summary>
    [DataField(required: true)]
    public CFRecipeCategory Category;

    [DataField]
    public TimeSpan CraftTime = TimeSpan.FromSeconds(1);

    [DataField(required: true)]
    public List<CFCraftRequirement> Requirements = new();

    [DataField(required: true)]
    public EntProtoId Result;

    [DataField]
    public int ResultCount = 1;

    /// <summary>The result is a rune. It is put on a matching item on the bench instead of being made. See <c>CFRuneSystem</c>.</summary>
    [DataField]
    public bool AttachRune;
}

/// <summary>
/// The kinds of recipe. Each has a name in <c>crafting.ftl</c> (<c>cf-recipe-category-tools</c>, ...).
/// </summary>
public enum CFRecipeCategory : byte
{
    Tools,
    Materials,
    Weapons,
    Armor,
    Clothing,
    Bags,
    Building,
    Food,
    Farming,
    Alchemy,
    Goods,
    Runes,
}
