using Content.IntegrationTests.Fixtures;
using Content.Server.CF.GOAP.DecisionLog;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;

/// <summary>
/// DEBUG, to be removed with the decision log. Prints what every brain decided after each test. The runner only shows
/// that output for a test that failed.
/// </summary>
public abstract class CFGOAPLoggedTest : GameTest
{
    [TearDown]
    public async Task PrintDecisionLogs()
    {
        var entities = Pair.Server.EntMan;
        await Pair.Server.WaitPost(() =>
        {
            entities.System<CFGOAPDecisionLogSystem>().WatchAll();
            var query = entities.AllEntityQueryEnumerator<CFGOAPDecisionLogComponent>();
            while (query.MoveNext(out var uid, out var log))
            {
                TestContext.Out.WriteLine($"Decisions of {entities.ToPrettyString(uid)}:");
                foreach (var entry in log.Entries)
                {
                    TestContext.Out.WriteLine($"  {entry}");
                }
            }
        });
    }
}
