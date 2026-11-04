using Content.Server.Stack;
using Content.Shared.CF.Temperature;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Stacks;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Shared.Timing;

namespace Content.Server.CF.Temperature;

public sealed partial class CFFireplaceSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ContainerSystem _containerSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private StackSystem _stackSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFFireplaceComponent, IgnitedEvent>(OnFireChanged);
    }

    private void OnFireChanged(Entity<CFFireplaceComponent> fireplace, ref IgnitedEvent args)
    {
        if (TryComp<FlammableComponent>(fireplace, out var flammable))
            flammable.FirestackFade = 0;
    }

    private bool TryFindFuel(Entity<CFFireplaceComponent> fireplace, out Entity<CFFireplaceFuelComponent>? fuel)
    {
        fuel = null;
        if (!_containerSystem.TryGetContainer(fireplace, fireplace.Comp.ContainerId, out var container))
            return false;

        foreach (var ent in container.ContainedEntities)
        {
            if (TryComp<CFFireplaceFuelComponent>(ent, out var component))
            {
                fuel = new Entity<CFFireplaceFuelComponent>(ent, component);
                return true;
            }
        }

        return false;
    }

    private void ConsumeFuel(EntityUid uid, CFFireplaceComponent fireplace, Entity<CFFireplaceFuelComponent> fuel)
    {
        fireplace.Fuel = Math.Min(fireplace.MaxFuelLimit, fireplace.Fuel + fuel.Comp.Fuel);
        _audio.PlayPvs(fireplace.InsertFuelSound, uid);

        if (TryComp<StackComponent>(fuel, out var stack))
            _stackSystem.SetCount(fuel, stack.Count - 1);
        else
            QueueDel(fuel);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CFFireplaceComponent, FlammableComponent>();
        while (query.MoveNext(out var uid, out var fireplace, out var flammable))
        {
            if (!flammable.OnFire || _timing.CurTime <= fireplace.NextUpdateTime)
                continue;

            fireplace.NextUpdateTime = _timing.CurTime + fireplace.UpdateFrequency;
            if (fireplace.Fuel >= fireplace.FuelDrainingPerUpdate)
            {
                fireplace.Fuel -= fireplace.FuelDrainingPerUpdate;
                flammable.FirestackFade = fireplace.FireFadeDelta;
                continue;
            }

            if (TryFindFuel(new Entity<CFFireplaceComponent>(uid, fireplace), out var fuel) && fuel != null)
                ConsumeFuel(uid, fireplace, fuel.Value);

            flammable.FirestackFade = -fireplace.FireFadeDelta;
            if (flammable.FireStacks <= 0 && fireplace.DeleteOnEmpty)
            {
                QueueDel(uid);
                break;
            }
        }
    }
}
