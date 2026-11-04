using Content.Shared.Containers;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Random;
using Content.Shared.Tools.Systems;
using Robust.Shared.Containers;

namespace Content.Server._CF.Materials;

public sealed class CFLogSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly RandomHelperSystem _randomHelper = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CFLogComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<CFLogComponent> ent, ref InteractUsingEvent args)
    {
        if (!_toolSystem.HasQuality(args.Used, ent.Comp.ToolQuality))
            return;

        var inContainer = _containerSystem.IsEntityInContainer(ent.Owner);
        var coordinates = Transform(ent.Owner).Coordinates;

        for (var i = 0; i < ent.Comp.SpawnCount; i++)
        {
            var plank = Spawn(ent.Comp.SpawnedPrototype, coordinates);

            if (inContainer)
            {
                _handsSystem.PickupOrDrop(args.User, plank);
                continue;
            }

            var xform = Transform(plank);
            _containerSystem.AttachParentToContainerOrGrid((plank, xform));
            xform.LocalRotation = 0;
            _randomHelper.RandomOffset(plank, 0.25f);
        }

        QueueDel(ent);
        args.Handled = true;
    }
}
