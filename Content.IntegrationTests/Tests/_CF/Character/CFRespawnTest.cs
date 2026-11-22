using Content.IntegrationTests.Fixtures;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking.Commands;
using Robust.Server.Console;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>On the alpha server anyone may send themselves back to the lobby to make a new character.</summary>
[TestFixture]
[TestOf(typeof(RespawnCommand))]
public sealed class CFRespawnTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task APlayerWhoIsNotAnAdminMayRespawn()
    {
        var session = Pair.Player!;
        var admins = Server.ResolveDependency<IAdminManager>();
        var groups = Server.ResolveDependency<IConGroupController>();

        await Server.WaitAssertion(() =>
        {
            admins.DeAdmin(session);
            Assert.That(admins.IsAdmin(session), Is.False);
            Assert.That(groups.CanCommand(session, "respawn"), "Anyone may respawn themselves.");
            Assert.That(groups.CanCommand(session, "kick"), Is.False, "Moderator commands stay closed.");
        });
    }
}
