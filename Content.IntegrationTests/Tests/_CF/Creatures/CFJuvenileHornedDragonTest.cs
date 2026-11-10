using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.Graphics.RSI;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>The Juvenile Horned Dragon: its immunities, and its sprite made for CarpFinder.</summary>
[TestFixture]
public sealed class CFJuvenileHornedDragonTest : GameTest
{
    private const string Dragon = "CFMobJuvenileHornedDragon";

    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task PoisonAndSleepDoNotTouchIt()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var hp = 0;
        var maxHp = -1;
        var asleep = true;
        await Server.WaitPost(() =>
        {
            var dragon = SEntMan.SpawnEntity(Dragon, map.Local(0, 0));
            var combat = SEntMan.GetComponent<CFCombatantComponent>(dragon);
            var conditions = SEntMan.System<CFConditionSystem>();

            SEntMan.System<CFEffectSystem>().DealDamage(dragon, dragon, 30, "Poison", false);
            conditions.ApplyCondition(dragon, CFConditionKind.Asleep, 0, 6f, dragon, 0);

            hp = combat.CurrentHp;
            maxHp = combat.MaxHp;
            asleep = conditions.IsProne(dragon);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hp, Is.EqualTo(maxHp), "Poison damage does nothing.");
            Assert.That(asleep, Is.False, "It cannot be put to sleep.");
        });
    }

    /// <summary>The sheet's rows are south, north, east, west, as the RSI wants them, and each animates.</summary>
    [Test]
    public async Task ItsSpriteLoads()
    {
        var hasAlive = false;
        var hasDead = false;
        var directions = RsiDirectionType.Dir1;
        var frames = 0;
        await Client.WaitPost(() =>
        {
            var cache = Client.ResolveDependency<IResourceCache>();
            if (!cache.TryGetResource<RSIResource>(new ResPath("/Textures/_CF/Mobs/Monsters/Dragon/horned_dragon.rsi"), out var rsi))
                return;

            hasAlive = rsi.RSI.TryGetState("alive", out var alive);
            hasDead = rsi.RSI.TryGetState("dead", out _);
            if (alive == null)
                return;

            directions = alive.RsiDirections;
            frames = alive.DelayCount;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasAlive);
            Assert.That(hasDead);
            Assert.That(directions, Is.EqualTo(RsiDirectionType.Dir4));
            Assert.That(frames, Is.EqualTo(4));
        });
    }
}
