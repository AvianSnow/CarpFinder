using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>
/// Adventure companies fight together. Two players fight only when both turn PvP on, and monsters can always be fought.
/// </summary>
[TestFixture]
[TestOf(typeof(CFFactionSystem))]
public sealed class CFPvpTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: CFPvpTestAlpha
  components:
  - type: CFFaction
    faction: Alpha
  - type: CFPvp

- type: entity
  id: CFPvpTestBeta
  components:
  - type: CFFaction
    faction: Beta
  - type: CFPvp

- type: entity
  id: CFPvpTestMerchant
  components:
  - type: CFFaction
    faction: Neutral
  - type: CFPvp

- type: entity
  id: CFPvpTestMonster
  components:
  - type: CFFaction
    faction: Monster
";

    [Test]
    public async Task PlayersFightOnlyWhenBothChoose()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var factions = SEntMan.System<CFFactionSystem>();
            var alpha = SEntMan.SpawnEntity("CFPvpTestAlpha", map.Local(0, 0));
            var beta = SEntMan.SpawnEntity("CFPvpTestBeta", map.Local(1, 0));
            var merchant = SEntMan.SpawnEntity("CFPvpTestMerchant", map.Local(0, 1));
            var alphaPvp = new Entity<CFPvpComponent>(alpha, SEntMan.GetComponent<CFPvpComponent>(alpha));
            var betaPvp = new Entity<CFPvpComponent>(beta, SEntMan.GetComponent<CFPvpComponent>(beta));

            Assert.Multiple(() =>
            {
                Assert.That(factions.CanHarm(alpha, beta), Is.False, "Companies are allies with PvP off.");
                Assert.That(factions.CanHarm(alpha, merchant), Is.False, "A merchant is a player too.");
                Assert.That(factions.GetAlliance(alpha, beta), Is.EqualTo(CFAlliance.Party));
            });

            Assert.That(factions.TrySetPvp(alphaPvp, true));
            Assert.That(factions.CanHarm(alpha, beta), Is.False, "One side alone is not enough.");

            Assert.That(factions.TrySetPvp(betaPvp, true));
            Assert.Multiple(() =>
            {
                Assert.That(factions.CanHarm(alpha, beta), Is.True);
                Assert.That(factions.CanHarm(beta, alpha), Is.True);
                Assert.That(factions.GetAlliance(alpha, beta), Is.EqualTo(CFAlliance.Opposition));
                Assert.That(factions.CanHarm(alpha, merchant), Is.False, "The merchant has PvP off.");
            });

            Assert.That(factions.TrySetPvp(alphaPvp, false), "No fight yet, so it can be turned off.");
            Assert.That(factions.CanHarm(alpha, beta), Is.False);
        });
    }

    [Test]
    public async Task PlayersAlwaysFightMonsters()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var factions = SEntMan.System<CFFactionSystem>();
            var alpha = SEntMan.SpawnEntity("CFPvpTestAlpha", map.Local(0, 0));
            var monster = SEntMan.SpawnEntity("CFPvpTestMonster", map.Local(1, 0));

            Assert.Multiple(() =>
            {
                Assert.That(factions.PvpEnabled(alpha), Is.False);
                Assert.That(factions.CanHarm(alpha, monster), Is.True);
                Assert.That(factions.CanHarm(monster, alpha), Is.True);
                Assert.That(factions.GetAlliance(alpha, monster), Is.EqualTo(CFAlliance.Opposition));
            });
        });
    }

    [Test]
    public async Task AFightKeepsPvpOn()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var factions = SEntMan.System<CFFactionSystem>();
            var alpha = SEntMan.SpawnEntity("CFPvpTestAlpha", map.Local(0, 0));
            var beta = SEntMan.SpawnEntity("CFPvpTestBeta", map.Local(1, 0));
            var alphaPvp = new Entity<CFPvpComponent>(alpha, SEntMan.GetComponent<CFPvpComponent>(alpha));
            var betaPvp = new Entity<CFPvpComponent>(beta, SEntMan.GetComponent<CFPvpComponent>(beta));
            factions.TrySetPvp(alphaPvp, true);
            factions.TrySetPvp(betaPvp, true);

            var hit = new CFDamagedEvent(alpha, 3);
            SEntMan.EventBus.RaiseLocalEvent(beta, ref hit);

            Assert.Multiple(() =>
            {
                Assert.That(factions.TrySetPvp(alphaPvp, false), Is.False, "The attacker is locked in.");
                Assert.That(factions.TrySetPvp(betaPvp, false), Is.False, "So is the one hit.");
                Assert.That(factions.PvpLockSeconds(alphaPvp), Is.EqualTo(CFFactionSystem.PvpLock.TotalSeconds));
                Assert.That(factions.PvpEnabled(alpha));
            });
        });
    }
}
