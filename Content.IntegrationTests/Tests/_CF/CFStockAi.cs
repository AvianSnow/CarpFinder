using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.CF;

/// <summary>
/// Every CF monster thinks with a GOAP brain, and the brain removes the HTN its body brought. A test that needs the same
/// body on stock HTN, to compare with it or to test a stock path, takes the brain out right after spawning and gives
/// it an HTN with a stock root task.
/// </summary>
public static class CFStockAi
{
    public static void MakeStock(IEntityManager entities, EntityUid mob, string rootTask = "SimpleHostileCompound")
    {
        entities.RemoveComponent<CFGOAPComponent>(mob);
        entities.RemoveComponent<CFGOAPSteeringComponent>(mob);
        var htn = entities.EnsureComponent<HTNComponent>(mob);
        htn.RootTask = new HTNCompoundTask { Task = rootTask };
        entities.System<NPCSystem>().WakeNPC(mob, htn);
    }
}
