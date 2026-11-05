using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Crafting;

[Serializable, NetSerializable]
public enum CFWorkbenchUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CFWorkbenchCraftMessage(ProtoId<CFCraftingRecipePrototype> recipe) : BoundUserInterfaceMessage
{
    public readonly ProtoId<CFCraftingRecipePrototype> Recipe = recipe;
}

/// <summary>One known recipe. <see cref="Blocked"/> is the locale id of why the user cannot attempt it.</summary>
[Serializable, NetSerializable]
public readonly record struct CFWorkbenchEntry(
    ProtoId<CFCraftingRecipePrototype> Recipe,
    int Dc,
    bool HasMaterials,
    string? Blocked);

[Serializable, NetSerializable]
public sealed class CFWorkbenchState(List<CFWorkbenchEntry> entries) : BoundUserInterfaceState
{
    public readonly List<CFWorkbenchEntry> Entries = entries;
}

[Serializable, NetSerializable]
public sealed partial class CFCraftDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public ProtoId<CFCraftingRecipePrototype> Recipe;

    public override DoAfterEvent Clone() => this;
}
