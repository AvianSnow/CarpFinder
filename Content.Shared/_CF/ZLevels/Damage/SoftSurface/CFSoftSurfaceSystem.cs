// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Robust.Shared.Analyzers;
namespace Content.Shared.CF.ZLevels.Damage.SoftSurface;

public sealed partial class CFSoftSurfaceSystem : EntitySystem
{

    [SubscribeLocalEvent]
    private void OnFallingDamageCalculate(Entity<CFSoftSurfaceComponent> ent, ref CFZFallingDamageCalculateEvent args)
    {
        if (args.Fallen == ent.Owner)
            return;

        args.DamageMultiplier *= ent.Comp.DamageMultiplier;
        args.StunMultiplier *= ent.Comp.StunMultiplier;
    }
}
