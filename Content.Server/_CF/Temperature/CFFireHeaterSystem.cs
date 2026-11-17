using Content.Shared.Atmos.Components;
using Content.Shared.CF.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Placeable;
using Content.Shared.Temperature.Components;
using Content.Shared.Temperature.Systems;
using Robust.Shared.Timing;

namespace Content.Server.CF.Temperature;

/// <summary>Once a second, a burning <see cref="CFFireHeaterComponent"/> heats the things set on it and the liquids in them.</summary>
public sealed partial class CFFireHeaterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTemperatureSystem _temperature = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var heaters = EntityQueryEnumerator<CFFireHeaterComponent, ItemPlacerComponent, FlammableComponent>();
        while (heaters.MoveNext(out _, out var heater, out var placer, out var flammable))
        {
            if (now < heater.NextUpdate)
                continue;

            heater.NextUpdate = now + TimeSpan.FromSeconds(1);
            if (!flammable.OnFire)
                continue;

            // A copy: heating can turn a thing into another, which changes the set.
            foreach (var placed in new List<EntityUid>(placer.PlacedEntities))
            {
                Heat(placed, heater);
            }
        }
    }

    private void Heat(EntityUid placed, CFFireHeaterComponent heater)
    {
        if (TerminatingOrDeleted(placed))
            return;

        var target = heater.SolutionDegrees;
        foreach (var (_, soln) in _solutions.EnumerateSolutions(placed))
        {
            var solution = soln.Comp.Solution;
            if (solution.Volume > 0 && solution.Temperature < target)
                _solutions.SetTemperature(soln, solution.Temperature + (target - solution.Temperature) / (float) solution.Volume);
        }

        // Stock heat is in joules against a heat capacity made for bodies, which leaves food on a fire lukewarm.
        // A fire here warms by degrees, the way a cook thinks of it.
        if (TryComp(placed, out TemperatureComponent? temperature))
        {
            var top = heater.MaxDegrees;
            if (temperature.Temperature < top)
                _temperature.SetCookedTemperature(placed, Math.Min(top, temperature.Temperature + heater.DegreesPerSecond));
        }

        if (TerminatingOrDeleted(placed))
            return;

        var ev = new CFHeatedEvent();
        RaiseLocalEvent(placed, ref ev);
    }
}
