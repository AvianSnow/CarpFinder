// Ported from Crystall Punk 14 (CP14CookingRecipePrototype and its requirements), MIT.
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// A dish a cooker makes. Nobody picks a recipe: the cooker takes the hardest one its ingredients fit, by the
/// tags on them and the reagents in it. Each food type has one recipe that always fits, for a mess.
/// </summary>
[Prototype("cookingRecipe")]
public sealed partial class CFCookingRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<CFCookingRequirement> Requirements = new();

    [DataField]
    public CFFoodData FoodData = new();

    [DataField(required: true)]
    public CFFoodType FoodType;

    [DataField]
    public TimeSpan CookingTime = TimeSpan.FromSeconds(20f);

    /// <summary>The sum of its requirements: recipes are tried from the highest down.</summary>
    public float Complexity()
    {
        var total = 0f;
        foreach (var requirement in Requirements)
        {
            total += requirement.Complexity();
        }

        return total;
    }
}

/// <summary>One thing a recipe asks of what is in the cooker.</summary>
[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class CFCookingRequirement
{
    public abstract bool IsMet(List<ProtoId<TagPrototype>> placedTags, Solution? solution);

    /// <summary>How hard this is to meet by chance. Harder recipes are tried first.</summary>
    public abstract float Complexity();
}

/// <summary>Some ingredient has one of these tags.</summary>
public sealed partial class CFTagRequired : CFCookingRequirement
{
    [DataField(required: true)]
    public List<ProtoId<TagPrototype>> Tags = new();

    [DataField]
    public bool AllowOtherTags = true;

    public override bool IsMet(List<ProtoId<TagPrototype>> placedTags, Solution? solution)
    {
        foreach (var tag in placedTags)
        {
            if (Tags.Contains(tag))
                return true;
        }

        return false;
    }

    public override float Complexity() => AllowOtherTags ? 5 : 1;
}

/// <summary>No ingredient has any of these tags.</summary>
public sealed partial class CFTagBlocked : CFCookingRequirement
{
    [DataField(required: true)]
    public HashSet<ProtoId<TagPrototype>> Tags = new();

    public override bool IsMet(List<ProtoId<TagPrototype>> placedTags, Solution? solution)
    {
        foreach (var tag in placedTags)
        {
            if (Tags.Contains(tag))
                return false;
        }

        return true;
    }

    public override float Complexity() => -Tags.Count;
}

/// <summary>The cooker's own solution holds this much of one of these reagents.</summary>
public sealed partial class CFReagentRequired : CFCookingRequirement
{
    [DataField(required: true)]
    public HashSet<ProtoId<ReagentPrototype>> Reagents = new();

    [DataField]
    public FixedPoint2 Amount = 10f;

    public override bool IsMet(List<ProtoId<TagPrototype>> placedTags, Solution? solution)
    {
        if (solution is null)
            return false;

        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (Reagents.Contains(reagent.Prototype) && quantity >= Amount)
                return true;
        }

        return false;
    }

    public override float Complexity() => 1;
}

/// <summary>Always fits. The fallback recipe of a food type has only this.</summary>
public sealed partial class CFAlwaysMet : CFCookingRequirement
{
    public override bool IsMet(List<ProtoId<TagPrototype>> placedTags, Solution? solution) => true;

    public override float Complexity() => 0;
}
