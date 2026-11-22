using Content.Server.CF.GOAP.Steps;
using Content.Shared.CF.GOAP;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Eats what it holds, a bite at a time, until the food is gone or thinking says it is full. Something its stomach
/// cannot take, such as a wheat bushel, ends the step at once instead of being chewed on for good. So does food the
/// tribe can still work on, such as dough with a fire burning or a loaf with a knife about: the plan said to prepare
/// it first, and ending here lets thinking plan that next stage.
/// </summary>
public sealed partial class CFGOAPEatStepSystem : CFGOAPStepSystem
{
    public static readonly TimeSpan BiteInterval = TimeSpan.FromSeconds(2.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPSocietySensorSystem _society = default!;
    [Dependency] private CFGOAPTribeSystem _tribes = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Eat;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out CFGOAPSocialComponent? social)
            || !_tribes.TryGetTribe(ent, out var tribe)
            || !_tribes.TryHeldFood(ent, out var food)
            || !_tribes.CanEat(ent, food))
            return true;

        if (_tribes.Stage(food) != CFFoodStage.Ready && _society.TryFindStation(ent, tribe.Comp, _transform.GetMapCoordinates(ent), food, out _))
            return true;

        Steering.Stop(ent.Owner);
        var now = _timing.CurTime;
        if (now < social.NextBite)
            return false;

        social.NextBite = now + BiteInterval;
        return !_ingestion.TryIngest(ent, food);
    }
}
