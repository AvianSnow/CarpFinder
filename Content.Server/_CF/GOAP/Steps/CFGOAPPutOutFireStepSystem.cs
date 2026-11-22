using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Drops and rolls, which is what a player does by clicking the on fire alert. Each roll leaves it paralyzed for a moment,
/// so it drops what it holds, and a big fire takes several.
/// </summary>
public sealed partial class CFGOAPPutOutFireStepSystem : CFGOAPStepSystem
{
    [Dependency] private FlammableSystem _flammable = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.PutOutFire;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out FlammableComponent? fire) || !fire.OnFire)
            return true;

        // It refuses while a roll is still going, so this starts the next one as soon as it can.
        _flammable.Resist(ent, fire);
        return false;
    }
}
