using Content.Server.CF.GOAP.Steps;
using Content.Shared.CF.GOAP;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>Eats what it holds, a bite at a time, until the food is gone or thinking says it is full.</summary>
public sealed partial class CFGOAPEatStepSystem : CFGOAPStepSystem
{
    public static readonly TimeSpan BiteInterval = TimeSpan.FromSeconds(2.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private CFGOAPTribeSystem _tribes = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Eat;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out CFGOAPSocialComponent? social) || !_tribes.TryHeldFood(ent, out var food))
            return true;

        Steering.Stop(ent.Owner);
        var now = _timing.CurTime;
        if (now < social.NextBite)
            return false;

        social.NextBite = now + BiteInterval;
        _ingestion.TryIngest(ent, food);
        return false;
    }
}
