// Ported from Crystall Punk 14 (CP14FoodCookerComponent, CP14FoodHolderComponent), MIT.
using Content.Shared.DoAfter;
using Content.Shared.Nutrition;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Cooking;

/// <summary>What a cooker makes, and what a dish can be served in: a pan's meal goes on a plate, a pot's soup in a bowl.</summary>
public enum CFFoodType : byte
{
    Meal,
    Soup,
    Pie,
}

/// <summary>
/// A pot, pan or pie plate. Ingredients go in its storage, and while it is being heated it cooks them into the
/// dish of the hardest <c>cookingRecipe</c> they fit. The dish is kept on its <see cref="CFFoodHolderComponent"/>.
/// Left on the heat after that, the dish burns.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), Access(typeof(CFSharedCookerSystem))]
public sealed partial class CFFoodCookerComponent : Component
{
    [DataField(required: true)]
    public CFFoodType FoodType;

    [DataField]
    public string ContainerId = "storagebase";

    [DataField]
    public string SolutionId = "cooker";

    public DoAfterId? DoAfterId;

    /// <summary>When it was last heated. Cooking stops when this is older than <see cref="HeatingFrequencyRequired"/>.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LastHeatingTime = TimeSpan.Zero;

    /// <summary>How often it has to be heated for the cooking not to stop. How hot does not matter, only for how long.</summary>
    [DataField]
    public TimeSpan HeatingFrequencyRequired = TimeSpan.FromSeconds(2f);

    /// <summary>How long a finished dish can stay on the heat before it burns.</summary>
    [DataField]
    public TimeSpan BurnTime = TimeSpan.FromSeconds(20f);

    /// <summary>Sometimes spawned where a dish burns.</summary>
    [DataField]
    public EntProtoId? BurntAdditionalSpawn = "CFFire";

    [DataField]
    public float BurntAdditionalSpawnProb = 0.2f;

    [DataField]
    public bool RenameCooker;
}

/// <summary>
/// Holds a cooked dish: a cooker gives it (<see cref="CanGiveFood"/>) and a plate or bowl takes it
/// (<see cref="CanAcceptFood"/>). The reagents are in a solution. This is the rest: name, look, flavors, trash.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true, raiseAfterAutoHandleState: true), Access(typeof(CFSharedCookerSystem))]
public sealed partial class CFFoodHolderComponent : Component
{
    [DataField, AutoNetworkedField]
    public CFFoodData? FoodData;

    [DataField]
    public bool CanAcceptFood;

    [DataField]
    public bool CanGiveFood;

    [DataField(required: true)]
    public CFFoodType FoodType;

    [DataField]
    public string SolutionId = "cooker";

    [DataField]
    public int MaxDisplacementFillLevels = 8;

    [DataField]
    public string? DisplacementRsiPath;

    /// <summary>The sprite layer the dish's layers are added at, so they sit between the fixed ones.</summary>
    [DataField]
    public string TargetLayerMap = "cp14_foodLayers";

    public HashSet<string> RevealedLayers = new();
}

/// <summary>Everything about a dish that its reagents cannot say.</summary>
[Serializable, NetSerializable]
[DataDefinition]
public sealed partial class CFFoodData
{
    public CFFoodData()
    {
    }

    public CFFoodData(CFFoodData data)
    {
        CurrentRecipe = data.CurrentRecipe;
        Name = data.Name;
        Desc = data.Desc;
        Visuals = new List<PrototypeLayerData>(data.Visuals);
        Trash = new List<EntProtoId>(data.Trash);
        Flavors = new HashSet<ProtoId<FlavorPrototype>>(data.Flavors);
        SliceProto = data.SliceProto;
        SliceCount = data.SliceCount;
    }

    [DataField]
    public ProtoId<CFCookingRecipePrototype>? CurrentRecipe;

    [DataField]
    public LocId? Name;

    [DataField]
    public LocId? Desc;

    [DataField]
    public List<PrototypeLayerData> Visuals = new();

    [DataField]
    public List<EntProtoId> Trash = new();

    [DataField]
    public HashSet<ProtoId<FlavorPrototype>> Flavors = new();

    /// <summary>What a knife cuts the served dish into, such as a pie into pieces.</summary>
    [DataField]
    public EntProtoId? SliceProto;

    [DataField]
    public ushort SliceCount = 5;
}

[Serializable, NetSerializable]
public enum CFCookingVisuals : byte
{
    Cooking,
    Burning,
}

[Serializable, NetSerializable]
public sealed partial class CFCookingDoAfter : DoAfterEvent
{
    [DataField]
    public ProtoId<CFCookingRecipePrototype> Recipe;

    public CFCookingDoAfter(ProtoId<CFCookingRecipePrototype> recipe)
    {
        Recipe = recipe;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class CFBurningDoAfter : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CFSliceDishDoAfter : SimpleDoAfterEvent;

/// <summary>Raised on a thing each second a fire under it burns (<c>CFFireHeaterSystem</c>).</summary>
[ByRefEvent]
public readonly record struct CFHeatedEvent;
