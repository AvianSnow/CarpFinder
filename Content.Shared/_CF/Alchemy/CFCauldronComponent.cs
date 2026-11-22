using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// A cauldron that brews potions. Its ingredients go in its storage and its water in its solution. Set on a fire,
/// it boils for a <c>craftingRecipe</c> its contents fit, then the brewer (whoever last put something in) rolls
/// Crafting against the item's DC, as at a workbench: the recipes are those with the cauldron's <c>CFWorkbench</c>
/// tag. After CP14's cauldron, which only heated a solution; the brewing is PF2e's Craft activity.
/// </summary>
[RegisterComponent]
public sealed partial class CFCauldronComponent : Component
{
    [DataField]
    public string ContainerId = "storagebase";

    [DataField]
    public string SolutionId = "cauldron";

    /// <summary>What each brew boils in, and how much of it.</summary>
    [DataField]
    public ProtoId<ReagentPrototype> Water = "Water";

    [DataField]
    public int WaterPerBrew = 10;

    /// <summary>A critical failure turns the brew's water into this.</summary>
    [DataField]
    public ProtoId<ReagentPrototype> SpoiledReagent = "CFSpoiledBrew";

    /// <summary>Whoever last put an ingredient or water in. They roll Crafting when the brew is done.</summary>
    [DataField]
    public EntityUid? Brewer;

    [DataField]
    public TimeSpan LastHeatingTime = TimeSpan.Zero;

    /// <summary>How often it must be heated for the boil not to stop.</summary>
    [DataField]
    public TimeSpan HeatingFrequencyRequired = TimeSpan.FromSeconds(2);

    /// <summary>When it may next tell the brewer why a brew cannot start.</summary>
    [DataField]
    public TimeSpan NextComplaint = TimeSpan.Zero;

    public DoAfterId? DoAfterId;
}

[Serializable, NetSerializable]
public sealed partial class CFBrewDoAfterEvent : DoAfterEvent
{
    [DataField]
    public string Recipe = string.Empty;

    public CFBrewDoAfterEvent(string recipe)
    {
        Recipe = recipe;
    }

    public override DoAfterEvent Clone() => this;
}
