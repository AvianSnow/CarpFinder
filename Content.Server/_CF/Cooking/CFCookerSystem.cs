// Ported from Crystall Punk 14 (CP14CookingSystem), MIT.
using Content.Shared.CF.Cooking;
using Content.Shared.CF.Temperature;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Temperature.Systems;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CF.Cooking;

/// <summary>The server's half of pot and pan cooking: ingredients that cook on their own, slicing, and the fire a burnt dish can start.</summary>
public sealed partial class CFCookerSystem : CFSharedCookerSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTemperatureSystem _temperature = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    private static readonly ProtoId<ToolQualityPrototype> Slicing = "Slicing";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFFoodHolderComponent, SolutionChangedEvent>(OnHolderChanged);
        SubscribeLocalEvent<CFFoodHolderComponent, CFSliceDishDoAfter>(OnSliced);
    }

    /// <summary>An emptied plate or pot is no longer the dish.</summary>
    private void OnHolderChanged(Entity<CFFoodHolderComponent> ent, ref SolutionChangedEvent args)
    {
        if (ent.Comp.FoodData is null || args.Solution.Comp.Solution.Volume != 0)
            return;

        ent.Comp.FoodData = null;
        Dirty(ent);
    }

    /// <summary>A knife on a dish with a <see cref="CFFoodData.SliceProto"/>, such as a baked pie, cuts it.</summary>
    protected override bool TrySlice(Entity<CFFoodHolderComponent> dish, EntityUid tool, EntityUid user)
    {
        if (dish.Comp.FoodData?.SliceProto is null || !_tool.HasQuality(tool, Slicing))
            return false;

        return _tool.UseTool(tool, user, dish, 2f, Slicing, new CFSliceDishDoAfter());
    }

    /// <summary>The dish becomes its pieces, and a mould it was baked in is left empty beside them.</summary>
    private void OnSliced(Entity<CFFoodHolderComponent> ent, ref CFSliceDishDoAfter args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.FoodData?.SliceProto is not { } slice)
            return;

        args.Handled = true;
        var coordinates = Transform(ent).Coordinates;
        for (var i = 0; i < ent.Comp.FoodData.SliceCount; i++)
        {
            SpawnAtPosition(slice, coordinates);
        }

        if (HasComp<CFFoodCookerComponent>(ent) && MetaData(ent).EntityPrototype is { } mould)
            SpawnAtPosition(mould.ID, coordinates);

        QueueDel(ent);
    }

    protected override void OnCookBurned(Entity<CFFoodCookerComponent> ent, ref CFBurningDoAfter args)
    {
        var burnt = !args.Cancelled && !args.Handled;
        base.OnCookBurned(ent, ref args);

        if (burnt && ent.Comp.BurntAdditionalSpawn is { } spawn && _random.Prob(ent.Comp.BurntAdditionalSpawnProb))
            SpawnAtPosition(spawn, Transform(ent).Coordinates);
    }

    protected override void OnCookFinished(Entity<CFFoodCookerComponent> ent, ref CFCookingDoAfter args)
    {
        // Raw things in the pot are cooked first, so the dish gets cooked meat's reagents and not raw meat's.
        if (!args.Cancelled && !args.Handled)
            CookContents(ent);

        base.OnCookFinished(ent, ref args);
    }

    private void CookContents(Entity<CFFoodCookerComponent> ent)
    {
        if (!Containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return;

        foreach (var contained in new List<EntityUid>(container.ContainedEntities))
        {
            if (!TryComp(contained, out CFTemperatureTransformationComponent? transformable)
                || !transformable.AutoTransformOnCooked
                || transformable.Entries.Count == 0)
            {
                continue;
            }

            var range = transformable.Entries[0].TemperatureRange;
            _temperature.SetCookedTemperature(contained, (range.X + range.Y) / 2);
        }
    }
}
