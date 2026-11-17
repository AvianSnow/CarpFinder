// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.Projectiles;

namespace Content.Shared.CF.ZLevels.Throwing;

/// <summary>
/// Keeps z-physics out of the way of a shot physical projectile (arrows, bolts).
/// The gun system launches projectiles with BodyStatus.InAir so tile friction doesn't apply,
/// but z-physics would sync BodyStatus back to OnGround (the projectile is at ground height),
/// making it slide to a stop after a few tiles. Z-physics is resumed once the projectile embeds.
/// </summary>
public sealed partial class CFZLevelProjectileSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnShot(Entity<CFZPhysicsComponent> ent, ref ProjectileShotEvent args)
    {
        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(CFZPhysicsComponent.Disabled));
    }

    [SubscribeLocalEvent]
    private void OnEmbed(Entity<CFZPhysicsComponent> ent, ref EmbedEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(CFZPhysicsComponent.Disabled));
    }
}
