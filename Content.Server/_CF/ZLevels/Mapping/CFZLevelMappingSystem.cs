// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Server.CF.ZLevels.Core;
using Content.Shared.CF.ZLevels.Core.Components;
using Robust.Shared.Analyzers;
using Robust.Shared.Map.Components;

namespace Content.Server.CF.ZLevels.Mapping;

public sealed partial class CFZLevelMappingSystem : EntitySystem
{
    [Dependency] private CFZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;

    [SubscribeLocalEvent]
    private void OnAddedIntoZNetwork(Entity<CFZMapComponent> ent, ref CFMapAddedIntoZNetworkEvent args)
    {
        if (_map.IsInitialized(ent))
            EntityManager.AddComponents(ent, args.Network.Comp.Components);
        else
        {
            var hasInitializedMaps = false;
            foreach (var existingMapUid in args.Network.Comp.ZLevels.Values)
            {
                if (existingMapUid.HasValue && _map.IsInitialized(existingMapUid.Value))
                {
                    hasInitializedMaps = true;
                    break;
                }
            }

            if (hasInitializedMaps)
                _map.InitializeMap(ent.Owner);
        }
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFZMapComponent> ent, ref MapInitEvent args)
    {
        if (!_zLevels.TryGetMapNetwork(ent, out var network))
            return;

        EntityManager.AddComponents(ent, network.Comp.Components);
    }
}
