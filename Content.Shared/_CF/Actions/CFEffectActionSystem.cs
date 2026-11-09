using Content.Shared.CF.Effects;
using Robust.Shared.Network;

namespace Content.Shared.CF.Actions;

/// <summary>Runs actions that only apply effects. See <see cref="CFEffectActionComponent"/>.</summary>
public sealed partial class CFEffectActionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFEffectActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            _effects.ResolveTarget(ent, args.Performer, args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFEffectActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            _effects.ResolveTarget(ent, args.Performer, args.Target);
    }
}
