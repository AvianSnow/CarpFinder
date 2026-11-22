using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Locks;
using Content.Shared.Lock;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Locks;

/// <summary>
/// The locks and keys ported from CP14: every keyed lock has a key, and a ring opens what its keys open.
/// </summary>
[TestFixture]
[TestOf(typeof(CFLockSystem))]
public sealed class CFPortedLockTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private List<EntityPrototype> Spawnable()
    {
        return SProtoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && p.ID.StartsWith("CF"))
            .ToList();
    }

    [Test]
    public async Task EveryKeyedLockHasAKey()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var keys = new HashSet<string>();
            foreach (var proto in Spawnable())
            {
                if (proto.TryComp<CFKeyComponent>(out var key, factory))
                    keys.Add(key.KeyId);
            }

            foreach (var proto in Spawnable())
            {
                if (proto.TryComp<CFLockComponent>(out var cfLock, factory) && cfLock.KeyId != null)
                    Assert.That(keys, Does.Contain(cfLock.KeyId), $"{proto.ID} has no key");
            }
        });
    }

    [Test]
    public async Task NoCFEntityKeepsAStockLock()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            foreach (var proto in Spawnable())
            {
                if (!proto.TryComp<LockComponent>(out var stock, factory))
                    continue;

                Assert.That(proto.TryComp<CFLockComponent>(out _, factory), $"{proto.ID} has a stock lock only");
                Assert.That(stock.UseAccess, Is.False, $"{proto.ID} still reads ID access");
            }
        });
    }

    [Test]
    public async Task PortedDoorOpensWithItsRingAndNotAnother()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var locks = SEntMan.System<CFLockSystem>();
            var stock = SEntMan.System<LockSystem>();
            var door = SEntMan.SpawnEntity("CFWoodenDoorTavernStaff", map.Local(0, 0));
            var ent = new Entity<CFLockComponent>(door, SEntMan.GetComponent<CFLockComponent>(door));
            var mob = SEntMan.SpawnEntity("MobHuman", map.Local(1, 0));
            var innkeeper = SEntMan.SpawnEntity("CFKeyRingInnkeeper", map.Local(1, 0));
            var guard = SEntMan.SpawnEntity("CFKeyRingGuard", map.Local(1, 0));

            Assert.That(stock.IsLocked(door));
            Assert.That(locks.TryUseKeyRing(ent, mob, guard), Is.False);
            Assert.That(stock.IsLocked(door));
            Assert.That(locks.TryUseKeyRing(ent, mob, innkeeper));
            Assert.That(stock.IsLocked(door), Is.False);
        });
    }
}
