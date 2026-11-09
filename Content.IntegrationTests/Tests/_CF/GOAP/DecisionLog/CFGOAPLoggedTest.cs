using Content.IntegrationTests.Fixtures;
using Content.Server.CF.GOAP.DecisionLog;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;

/// <summary>
/// FOR DEBUGGING ONLY, and removed with the decision log. Prints what each brain decided at the end of a test. It writes
/// for every test, because a failure on the server thread can still look fine here (see <c>ai-testing.md</c>). The test
/// runner only shows the output of tests that failed, unless asked for more.
/// </summary>
public abstract class CFGOAPLoggedTest : GameTest
{
    public override async Task DoTeardown()
    {
        try
        {
            await Server.WaitPost(PrintDecisionLogs);
        }
        catch (Exception e)
        {
            // A test that failed can leave the server unable to answer. The real failure still has to come through.
            TestContext.Out.WriteLine($"No decision log: {e.Message}");
        }

        await base.DoTeardown();
    }

    private void PrintDecisionLogs()
    {
        SEntMan.System<CFGOAPDecisionLogSystem>().WatchAll();

        var query = SEntMan.AllEntityQueryEnumerator<CFGOAPDecisionLogComponent>();
        while (query.MoveNext(out var uid, out var log))
        {
            TestContext.Out.WriteLine($"Decision log of {SEntMan.ToPrettyString(uid)}:");
            foreach (var entry in log.Entries)
            {
                TestContext.Out.WriteLine($"  {entry}");
            }
        }
    }
}
