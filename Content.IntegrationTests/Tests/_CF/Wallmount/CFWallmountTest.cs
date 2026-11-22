using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Wallmount;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Wallmount;

/// <summary>
/// Stock replaced the Wall tag with <c>WallComponent</c>, and code that still asked for the tag quietly only found windows.
/// Asserts stay on the test thread, so a failure keeps its message.
/// </summary>
[TestFixture]
[TestOf(typeof(CFWallmountSystem))]
public sealed class CFWallmountTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestCase("CFWallStone", true)]
    [TestCase("WallSolid", true)]
    [TestCase("Window", true)]
    [TestCase("CrateGenericSteel", false)]
    public async Task ItKnowsAWallWhenItSeesOne(string id, bool wall)
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var result = !wall;
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity(id, map.Local(0, 0));
            result = CFWallmountSystem.IsWall(SEntMan, SEntMan.System<TagSystem>(), uid);
        });

        Assert.That(result, Is.EqualTo(wall));
    }

    [Test]
    public async Task AMountHangsOnARealWall()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid wall = default;
        EntityUid mount = default;
        await Server.WaitPost(() =>
        {
            mount = SEntMan.SpawnEntity("CFWallmountFlagAlchemist", map.Local(1, 0));
            var xform = SEntMan.GetComponent<TransformComponent>(mount);
            var behind = xform.LocalPosition - xform.LocalRotation.ToWorldVec().Normalized();
            wall = SEntMan.SpawnEntity("CFWallStone", new EntityCoordinates(map.Grid, behind));
        });

        await Pair.RunTicksSync(90);

        var attached = false;
        var alive = false;
        await Server.WaitPost(() =>
        {
            alive = SEntMan.EntityExists(mount);
            attached = SEntMan.HasComponent<CFWallmountedComponent>(wall);
        });

        Assert.That(alive, "It was not torn down for having no wall.");
        Assert.That(attached);
    }
}
