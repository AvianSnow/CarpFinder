// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core;
using Content.Shared.Damage.Systems;
using Content.Shared.Effects;
using Robust.Shared.Analyzers;

namespace Content.Shared.CF.ZLevels.Damage.FallingDamage;

public sealed partial class CFFallingDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;

    [SubscribeLocalEvent]
    private void OnFallOnMe(Entity<CFFallingDamageComponent> ent, ref CFZFellOnMeEvent args)
    {
        if (_damageable.TryChangeDamage(args.Fallen, ent.Comp.Damage * args.Speed))
            _colorFlash.RaiseEffect(Color.Red, new List<EntityUid> { args.Fallen }, CFFilter.ZPvsExcept(args.Fallen, EntityManager));
    }
}
