using Content.Shared.Materials;
using Content.Shared.Stacks;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Crafting;

/// <summary>
/// One raw material of a recipe, taken from what lies on the workbench.
/// Based on CP14's <c>CP14WorkbenchCraftRequirement</c>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class CFCraftRequirement
{
    /// <summary>How much the recipe needs, in this requirement's own units.</summary>
    [DataField]
    public int Count = 1;

    public abstract bool IsMet(IEntityManager entities, HashSet<EntityUid> placed);

    /// <summary>Uses up <paramref name="units"/> of it. A critical failure passes less than <see cref="Count"/>.</summary>
    public abstract void Consume(IEntityManager entities, HashSet<EntityUid> placed, int units);

    /// <summary>"iron bar x2", shown in the workbench window.</summary>
    public abstract string Title(IPrototypeManager prototypes);
}

/// <summary>A number of a stack type, such as iron bars.</summary>
public sealed partial class CFStackRequirement : CFCraftRequirement
{
    [DataField(required: true)]
    public ProtoId<StackPrototype> Stack;

    public override bool IsMet(IEntityManager entities, HashSet<EntityUid> placed)
    {
        var count = 0;
        foreach (var item in placed)
        {
            if (entities.TryGetComponent(item, out StackComponent? stack) && stack.StackTypeId == Stack)
                count += stack.Count;
        }

        return count >= Count;
    }

    public override void Consume(IEntityManager entities, HashSet<EntityUid> placed, int units)
    {
        var stacks = entities.System<SharedStackSystem>();
        foreach (var item in placed)
        {
            if (units <= 0)
                return;

            if (!entities.TryGetComponent(item, out StackComponent? stack) || stack.StackTypeId != Stack)
                continue;

            var used = Math.Min(units, stack.Count);
            units -= used;
            if (stack.Count <= used)
                entities.DeleteEntity(item);
            else
                stacks.SetCount((item, stack), stack.Count - used);
        }
    }

    public override string Title(IPrototypeManager prototypes)
    {
        return $"{Loc.GetString(prototypes.Index(Stack).Name)} x{Count}";
    }
}

/// <summary>A number of one entity prototype, such as string.</summary>
public sealed partial class CFItemRequirement : CFCraftRequirement
{
    [DataField(required: true)]
    public EntProtoId ProtoId;

    private bool Matches(IEntityManager entities, EntityUid item)
    {
        return entities.TryGetComponent(item, out MetaDataComponent? meta) && meta.EntityPrototype?.ID == ProtoId.Id;
    }

    public override bool IsMet(IEntityManager entities, HashSet<EntityUid> placed)
    {
        var count = 0;
        foreach (var item in placed)
        {
            if (Matches(entities, item))
                count++;
        }

        return count >= Count;
    }

    public override void Consume(IEntityManager entities, HashSet<EntityUid> placed, int units)
    {
        foreach (var item in placed)
        {
            if (units <= 0)
                return;

            if (!Matches(entities, item))
                continue;

            units--;
            entities.DeleteEntity(item);
        }
    }

    public override string Title(IPrototypeManager prototypes)
    {
        return $"{prototypes.Index(ProtoId).Name} x{Count}";
    }
}

/// <summary>An amount of a material in anything on the bench, such as the iron in ore.</summary>
public sealed partial class CFMaterialRequirement : CFCraftRequirement
{
    [DataField(required: true)]
    public ProtoId<MaterialPrototype> Material;

    public override bool IsMet(IEntityManager entities, HashSet<EntityUid> placed)
    {
        var count = 0;
        foreach (var item in placed)
        {
            if (!entities.TryGetComponent(item, out PhysicalCompositionComponent? composition)
                || !composition.MaterialComposition.TryGetValue(Material, out var value))
            {
                continue;
            }

            count += entities.TryGetComponent(item, out StackComponent? stack) ? value * stack.Count : value;
        }

        return count >= Count;
    }

    public override void Consume(IEntityManager entities, HashSet<EntityUid> placed, int units)
    {
        var stacks = entities.System<SharedStackSystem>();
        foreach (var item in placed)
        {
            if (units <= 0)
                return;

            if (!entities.TryGetComponent(item, out PhysicalCompositionComponent? composition)
                || !composition.MaterialComposition.TryGetValue(Material, out var value)
                || value <= 0)
            {
                continue;
            }

            if (!entities.TryGetComponent(item, out StackComponent? stack))
            {
                units -= value;
                entities.DeleteEntity(item);
                continue;
            }

            // Whole pieces only, so a part-used piece stays with the crafter.
            var pieces = Math.Min(stack.Count, Math.Max(1, units / value));
            units -= pieces * value;
            if (stack.Count <= pieces)
                entities.DeleteEntity(item);
            else
                stacks.SetCount((item, stack), stack.Count - pieces);
        }
    }

    public override string Title(IPrototypeManager prototypes)
    {
        return $"{Loc.GetString(prototypes.Index(Material).Name)} x{Count}";
    }
}
