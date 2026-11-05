using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Crafting;

/// <summary>
/// A crafting station. It offers the recipes with one of <see cref="RecipeTags"/> that the user knows,
/// and takes materials from what lies on it (<c>ItemPlacer</c>).
/// Based on CP14's <c>CP14WorkbenchComponent</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFWorkbenchComponent : Component
{
    [DataField]
    public List<ProtoId<TagPrototype>> RecipeTags = new();

    [DataField]
    public SoundSpecifier CraftSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
}

/// <summary>The recipes a creature has learned.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFFormulaBookComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<CFCraftingRecipePrototype>> Known = new();
}

/// <summary>
/// A written formula. Reading it in hand teaches its recipes and leaves it intact, so it can be passed around:
/// anyone can read one, and only crafting needs the Crafting skill.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFFormulaComponent : Component
{
    [DataField]
    public List<ProtoId<CFCraftingRecipePrototype>> Recipes = new();

    /// <summary>A basic crafter's book: also teaches every common level 0 recipe.</summary>
    [DataField]
    public bool AllBasic;
}
