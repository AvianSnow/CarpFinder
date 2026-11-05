using Content.IntegrationTests.Fixtures;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Items;
using Content.Shared.CF.Shop;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>The printed Player Core rows for ported gear, read from the prototypes.</summary>
[TestFixture]
public sealed class CFGearPortTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    private T Comp<T>(string id) where T : Component, new()
    {
        Assert.That(Server.ProtoMan.Index<EntityPrototype>(id).TryComp(out T? comp, SEntMan.ComponentFactory), id + " should have " + typeof(T).Name);
        return comp!;
    }

    [TestCase("CFDagger", 1, 4, "Piercing", 20, 0.1f)]
    [TestCase("CFRapier", 1, 6, "Piercing", 200, 1f)]
    [TestCase("CFScimitar", 1, 6, "Slashing", 100, 1f)]
    [TestCase("CFLongsword", 1, 8, "Slashing", 100, 1f)]
    [TestCase("CFGreatsword", 1, 12, "Slashing", 200, 2f)]
    [TestCase("CFBattleAxe", 1, 8, "Slashing", 100, 1f)]
    [TestCase("CFWarhammer", 1, 8, "Bludgeoning", 100, 1f)]
    [TestCase("CFSpear", 1, 6, "Piercing", 10, 1f)]
    [TestCase("CFHatchet", 1, 6, "Slashing", 40, 0.1f)]
    [TestCase("CFSickle", 1, 4, "Slashing", 20, 0.1f)]
    [TestCase("CFLightHammer", 1, 6, "Bludgeoning", 30, 0.1f)]
    [TestCase("CFPick", 1, 6, "Piercing", 70, 1f)]
    [TestCase("CFShortbow", 1, 6, "Piercing", 300, 1f)]
    [TestCase("CFLongbow", 1, 8, "Piercing", 600, 2f)]
    [TestCase("CFCrossbow", 1, 8, "Piercing", 300, 1f)]
    [TestCase("CFHeavyCrossbow", 1, 10, "Piercing", 400, 2f)]
    [TestCase("CFDart", 1, 4, "Piercing", 1, 0f)]
    public async Task WeaponMatchesTheBook(string id, int dice, int die, string type, int copper, float bulk)
    {
        await Server.WaitAssertion(() =>
        {
            var weapon = Comp<CFWeaponComponent>(id);
            Assert.Multiple(() =>
            {
                Assert.That(weapon.DamageDice, Is.EqualTo(dice));
                Assert.That(weapon.DamageDie, Is.EqualTo(die));
                Assert.That(weapon.DamageType.Id, Is.EqualTo(type));
                Assert.That(Comp<CFItemPriceComponent>(id).Copper, Is.EqualTo(copper));
                Assert.That(Comp<CFBulkComponent>(id).Bulk, Is.EqualTo(bulk));
            });
        });
    }

    [TestCase("CFChainShirt", 2, 3, -1, 0, 1)]
    [TestCase("CFHideArmor", 3, 2, -2, -5, 2)]
    [TestCase("CFChainMail", 4, 1, -2, -5, 3)]
    [TestCase("CFBreastplate", 4, 1, -2, -5, 3)]
    [TestCase("CFHalfPlate", 5, 1, -3, -10, 3)]
    public async Task ArmorMatchesTheBook(string id, int ac, int dex, int check, int speed, int strength)
    {
        await Server.WaitAssertion(() =>
        {
            var armor = Comp<CFArmorComponent>(id);
            Assert.Multiple(() =>
            {
                Assert.That(armor.AcBonus, Is.EqualTo(ac));
                Assert.That(armor.DexCap, Is.EqualTo(dex));
                Assert.That(armor.CheckPenalty, Is.EqualTo(check));
                Assert.That(armor.SpeedPenalty, Is.EqualTo(speed));
                Assert.That(armor.Strength, Is.EqualTo(strength));
            });
        });
    }

    [TestCase("CFWoodenShield", 2, 3, 12, 6)]
    [TestCase("CFBuckler", 1, 3, 6, 3)]
    [TestCase("CFTowerShield", 2, 5, 20, 10)]
    [TestCase("CFSteelShield", 2, 5, 20, 10)]
    public async Task ShieldMatchesTheBook(string id, int ac, int hardness, int hp, int broken)
    {
        await Server.WaitAssertion(() =>
        {
            var shield = Comp<CFShieldComponent>(id);
            Assert.Multiple(() =>
            {
                Assert.That(shield.AcBonus, Is.EqualTo(ac));
                Assert.That(shield.Hardness, Is.EqualTo(hardness));
                Assert.That(shield.MaxHp, Is.EqualTo(hp));
                Assert.That(shield.BrokenThreshold, Is.EqualTo(broken));
                Assert.That(Comp<CFWeaponComponent>(id).DamageDie, Is.EqualTo(4), "Shields bash for d4.");
            });
        });
    }

    [Test]
    public async Task TwoHandedWeaponsEnforceTheirHands()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            foreach (var proto in Server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryComp(out CFWeaponComponent? weapon, factory) || weapon.Hands != CFWeaponHands.Two)
                    continue;

                Assert.That(proto.TryComp(out WieldableComponent? _, factory), proto.ID + " must need wielding");
            }
        });
    }

    [Test]
    public async Task ShovelIsImprovised()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Comp<CFWeaponComponent>("CFShovel").Improvised);
            Assert.That(Comp<CFShieldComponent>("CFTowerShield").SpeedPenalty, Is.EqualTo(-5));
            Assert.That(Comp<CFShieldComponent>("CFTowerShield").TakeCoverBonus, Is.EqualTo(4));
        });
    }
}
