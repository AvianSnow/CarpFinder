using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.HTN;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.CF;

/// <summary>
/// Every CF monster thinks with a GOAP brain. A test that needs the same body on stock HTN, to compare with it or to
/// test a stock path, takes the brain out right after spawning, before HTN has planned anything.
/// </summary>
public static class CFStockAi
{
    public static void MakeStock(IEntityManager entities, EntityUid mob, string rootTask = "SimpleHostileCompound")
    {
        entities.RemoveComponent<CFGOAPComponent>(mob);
        entities.RemoveComponent<CFGOAPSteeringComponent>(mob);
        entities.GetComponent<HTNComponent>(mob).RootTask = new HTNCompoundTask { Task = rootTask };
    }
}
