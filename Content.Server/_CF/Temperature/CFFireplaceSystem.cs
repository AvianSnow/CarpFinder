using Content.Server.Atmos.EntitySystems;
using Content.Server.Stack;
using Content.Shared.CF.Temperature;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Stacks;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.Temperature;

/// <summary>
/// A fire that burns fuel: planks, logs, coal put in it. Each piece burnt leaves ash beside it, which is fertilizer.
/// A kobold tribe feeds its fire and relights it (<c>CFGOAPKeepNestStepSystem</c>).
/// </summary>
public sealed partial class CFFireplaceSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ContainerSystem _containerSystem = default!;
    [Dependency] private FlammableSystem _flammable = default!;
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

    /// <summary>Whether it is alight.</summary>
    public bool IsBurning(EntityUid fireplace)
    {
        return TryComp<FlammableComponent>(fireplace, out var flammable) && flammable.OnFire;
    }

    /// <summary>
    /// Whether it wants fuel: it is out, or what it has to burn is below <paramref name="below"/> and nothing more
    /// waits in it. A fire with fuel in it feeds itself.
    /// </summary>
    public bool NeedsFuel(Entity<CFFireplaceComponent> fireplace, float below)
    {
        if (!IsBurning(fireplace))
            return true;

        return fireplace.Comp.Fuel < below && !TryFindFuel(fireplace, out _);
    }

    /// <summary>Whether another piece of fuel would not just be wasted: it is not full, and nothing waits in it.</summary>
    public bool HasRoom(Entity<CFFireplaceComponent> fireplace)
    {
        return fireplace.Comp.Fuel < fireplace.Comp.MaxFuelLimit && !TryFindFuel(fireplace, out _);
    }

    /// <summary>Puts a piece of fuel in, and lights the fire if it is out. False when the thing is not fuel.</summary>
    public bool TryFeed(Entity<CFFireplaceComponent> fireplace, EntityUid fuel, EntityUid? user)
    {
        if (!HasComp<CFFireplaceFuelComponent>(fuel)
            || !_containerSystem.TryGetContainer(fireplace, fireplace.Comp.ContainerId, out var container)
            || !_containerSystem.Insert(fuel, container))
            return false;

        if (!IsBurning(fireplace))
            _flammable.Ignite(fireplace, user ?? fireplace.Owner, ignitionSourceUser: user);

        return true;
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
            _stackSystem.SetCount((fuel, stack), stack.Count - 1);
        else
            QueueDel(fuel);

        // What is left of it falls beside the fire.
        if (fireplace.Ash is { } ash && _random.Prob(fireplace.AshChance))
            Spawn(ash, Transform(uid).Coordinates.Offset(_random.NextAngle().ToVec()));
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
