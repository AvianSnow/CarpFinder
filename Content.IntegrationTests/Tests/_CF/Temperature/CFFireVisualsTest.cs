using Content.Client.Atmos.Components;
using Content.Client.Atmos.EntitySystems;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Temperature;

/// <summary>What a client does with something already burning when it first sees it.</summary>
[TestFixture]
[TestOf(typeof(FireVisualizerSystem))]
public sealed class CFFireVisualsTest : GameTest
{
    private static readonly Box2i Footprint = new(0, 0, 1, 1);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    /// Lit on the tick it spawns, so the client's first state already says it burns. The light is a child entity,
    /// and spawning it while the torch was still initializing crashed a Debug client.
    /// </summary>
    [Test]
    public async Task AClientFirstSeesATorchAlreadyBurning()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var torch = EntityUid.Invalid;

        await Pair.Server.WaitPost(() =>
        {
            torch = SEntMan.SpawnAttachedTo("CFTorch", map.Local(0, 0));
            SEntMan.System<FlammableSystem>().AdjustFireStacks(torch, 5f, ignite: true);
        });

        await Pair.RunTicksSync(10);

        var hasLight = false;
        await Pair.Client.WaitPost(() =>
        {
            hasLight = CEntMan.GetComponent<FireVisualsComponent>(ToClientUid(torch)).LightEntity != null;
        });

        Assert.That(hasLight, "The burning torch should have its light");
    }
}
