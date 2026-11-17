// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using System.Numerics;
using Content.Client.CF.ZLevels.Core;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Flight;
using Content.Shared.CF.ZLevels.Flight.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client.CF.ZLevels.Flight;

public sealed partial class CFClientZFlightSystem : CFSharedZFlightSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CFZFlyerComponent, CFZPhysicsComponent, TransformComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var flyer, out var zPhys, out var xform, out var sprite))
        {
            if (!flyer.Active)
                continue;
            if (_timing.CurTime < flyer.NextVfx)
                continue;
            flyer.NextVfx = _timing.CurTime + TimeSpan.FromSeconds(0.2f);

            if (flyer.FlightVfx is not null)
            {
                var vfx = SpawnAtPosition(flyer.FlightVfx, xform.Coordinates);
                _sprite.SetOffset(vfx, new Vector2(0, zPhys.LocalPosition * CFClientZLevelsSystem.ZLevelOffset) + zPhys.SpriteOffsetDefault);
            }
        }
    }
}
