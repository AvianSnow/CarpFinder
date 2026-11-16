using Content.Shared.CF.GOAP;
using Content.Shared.Temperature.Systems;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Holds raw food over the tribe's fire until it is cooked. The heat is what a grill gives it, set the way the
/// Prestidigitation cantrip does, so the food's own cooking recipe turns it into the cooked thing.
/// </summary>
public sealed partial class CFGOAPCookStepSystem : CFGOAPErrandStepSystem
{
    private const float CookTemperature = 420f;

    [Dependency] private SharedTemperatureSystem _temperature = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Cook;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(4);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Tribes.TryGetFire(errand.Tribe.Comp, out target);
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.Begin(ent, action);
        if (TryGetErrand(ent, out var errand) && errand.Social.Errand is { } fire)
            Tribes.Release(errand.Tribe.Comp, fire, ent);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (Tribes.TryHeldFood(errand.Uid, out var food) && Society.IsRaw(food))
            _temperature.SetCookedTemperature(food, CookTemperature);

        return true;
    }
}
