using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>
/// Phase 2 item rules: weapon traits from <c>cfTrait</c> rules, runes, critical damage, armor penalties,
/// shields, and Bulk. Runs on the dev map (<c>Resources/Prototypes/Maps/debug.yml</c>).
/// </summary>
[TestFixture]
[TestOf(typeof(CFWeaponSystem))]
[TestOf(typeof(CFArmorSystem))]
[TestOf(typeof(CFShieldSystem))]
public sealed class CFItemRulesTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFItemTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 20
    currentHp: 20
    armorFromEquipment: true
  - type: CFAbilityScore

- type: entity
  parent: CFLeatherArmor
  id: CFItemTestFullPlate
  components:
  - type: CFArmor
    category: Heavy
    group: Plate
    acBonus: 6
    dexCap: 0
    checkPenalty: -3
    speedPenalty: -10
    strength: 4
  - type: CFTrait
    traits:
    - Noisy
";

    private CFDevMap _map = default!;

    private EntityUid Human(int strength = 0, int dexterity = 0)
    {
        var mob = SEntMan.SpawnEntity("CFItemTestHuman", _map.Local(0, 0));
        var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(mob);
        abilities.AbilityModifiers[CFAbility.Strength] = strength;
        abilities.AbilityModifiers[CFAbility.Dexterity] = dexterity;
        return mob;
    }

    private void Wear(EntityUid mob, string armor)
    {
        var item = SEntMan.SpawnEntity(armor, _map.Local(0, 0));
        var inventory = SEntMan.System<InventorySystem>();
        Assert.That(inventory.TryEquip(mob, item, "outerClothing", silent: true, force: true), $"Could not wear {armor}.");
    }

    [Test]
    public async Task WeaponTraitsComeFromTraitRules()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();

            var bow = SEntMan.SpawnEntity("CFLongbow", _map.Local(0, 0));
            Assert.That(weapons.TryStats(bow, out var bowStats));
            Assert.That(bowStats.Ranged);
            Assert.That(bowStats.DamageDie, Is.EqualTo(8));
            Assert.That(bowStats.RangeIncrementFeet, Is.EqualTo(100));
            Assert.That(bowStats.Proficiency, Is.EqualTo("MartialWeapons"));
            Assert.That(bowStats.Traits.Deadly, Is.EqualTo(10));
            Assert.That(bowStats.Traits.Volley, Is.EqualTo(30));

            var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            Assert.That(weapons.TryStats(sword, out var swordStats));
            Assert.That(swordStats.Ranged, Is.False);
            Assert.That(swordStats.Traits.Versatile, Does.Contain("Piercing"));

            // The sling's own trait list replaces the longbow parent's.
            var sling = SEntMan.SpawnEntity("CFSling", _map.Local(0, 0));
            Assert.That(weapons.TryStats(sling, out var slingStats));
            Assert.That(slingStats.Traits.Propulsive);
            Assert.That(slingStats.Traits.Deadly, Is.EqualTo(0));
            Assert.That(slingStats.Reload, Is.EqualTo(1));

            var spear = SEntMan.SpawnEntity("CFSpear", _map.Local(0, 0));
            Assert.That(weapons.TryStats(spear, out var thrown, thrown: true));
            Assert.That(thrown.Ranged && thrown.ThrownStrike);
            Assert.That(thrown.RangeIncrementFeet, Is.EqualTo(20));
        });
    }

    [Test]
    public async Task RunesAddPotencyAndStrikingDice()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            var runes = SEntMan.EnsureComponent<CFWeaponRunesComponent>(sword);
            runes.Potency = 1;
            runes.Striking = 2;
            runes.Property = new List<ProtoId<CFPropertyRunePrototype>> { "Flaming", "Frost" };

            Assert.That(weapons.TryStats(sword, out var stats));
            Assert.That(stats.Potency, Is.EqualTo(1));
            Assert.That(stats.DamageDice, Is.EqualTo(3), "Greater striking adds two dice.");

            var active = new List<string>();
            foreach (var rune in weapons.PropertyRunes(sword))
            {
                active.Add(rune.ID);
            }

            Assert.That(active, Is.EqualTo(new[] { "Flaming" }), "A +1 weapon holds one property rune.");
        });
    }

    [Test]
    public async Task CriticalHitsDoubleThenAddDeadly()
    {
        await Server.WaitAssertion(() =>
        {
            var random = Server.ResolveDependency<IRobustRandom>();
            var deadly = new CFWeaponTraits { Deadly = 10 };
            var bow = new CFStrikeWeaponStats(1, 8, "Piercing", "MartialWeapons", deadly, Ranged: true);

            var low = int.MaxValue;
            var high = int.MinValue;
            for (var i = 0; i < 500; i++)
            {
                var damage = CFMelee.RollStrikeDamage(random, bow, damageMod: 3, critical: true);
                low = Math.Min(low, damage);
                high = Math.Max(high, damage);
            }

            // (1d8 + 3) × 2 + 1d10: 9 to 32.
            Assert.That(low, Is.GreaterThanOrEqualTo(9));
            Assert.That(high, Is.LessThanOrEqualTo(32));

            var fist = new CFStrikeWeaponStats(1, 4, "Bludgeoning", "Unarmed", CFWeaponTraits.Fist);
            Assert.That(CFMelee.RollStrikeDamage(random, fist, 0, critical: false), Is.InRange(1, 4));
            Assert.That(CFMelee.ForcefulBonus(fist, 2), Is.EqualTo(0), "Fists are not forceful.");
        });
    }

    [Test]
    public async Task ArmorSetsAcAndPenaltiesUntilStrengthIsMet()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var armor = SEntMan.System<CFArmorSystem>();

            var weak = Human(strength: 0, dexterity: 3);
            Wear(weak, "CFItemTestFullPlate");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(weak);
            Assert.That(combat.ArmorClass, Is.EqualTo(10 + 0 + 6), "Dex cap +0, untrained, full plate +6.");
            Assert.That(armor.CheckPenalty(weak, "Athletics", CFAbility.Strength, attackTrait: false), Is.EqualTo(-3));
            Assert.That(armor.CheckPenalty(weak, "Athletics", CFAbility.Strength, attackTrait: true), Is.EqualTo(0));
            Assert.That(armor.CheckPenalty(weak, "Diplomacy", CFAbility.Charisma, attackTrait: false), Is.EqualTo(0));
            Assert.That(armor.SpeedPenalty(weak), Is.EqualTo(-10));

            var strong = Human(strength: 4);
            Wear(strong, "CFItemTestFullPlate");
            Assert.That(armor.CheckPenalty(strong, "Athletics", CFAbility.Strength, attackTrait: false), Is.EqualTo(0));
            Assert.That(armor.CheckPenalty(strong, "Stealth", CFAbility.Dexterity, attackTrait: false), Is.EqualTo(-3),
                "Noisy armor keeps its penalty on Stealth.");
            Assert.That(armor.SpeedPenalty(strong), Is.EqualTo(-5), "Meeting Strength cuts the Speed penalty by 5 feet.");

            var leather = Human(dexterity: 5);
            Wear(leather, "CFLeatherArmor");
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(leather).ArmorClass, Is.EqualTo(10 + 4 + 1),
                "Leather caps Dexterity at +4.");
        });
    }

    [Test]
    public async Task ArmorRunesAreItemBonuses()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = Human();
            var item = SEntMan.SpawnEntity("CFLeatherArmor", _map.Local(0, 0));
            var runes = SEntMan.EnsureComponent<CFArmorRunesComponent>(item);
            runes.Potency = 1;
            runes.Resilient = 1;
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(mob, item, "outerClothing", silent: true, force: true));

            var bonuses = SEntMan.System<CFBonusSystem>();
            Assert.That(bonuses.Total(mob, CFBonusStat.ArmorClass), Is.EqualTo(1));
            Assert.That(bonuses.Total(mob, CFBonusStat.Saves), Is.EqualTo(1));

            // Mystic armor is also an item bonus to AC, so it does not stack with the potency rune.
            bonuses.AddBonus(mob, CFBonusStat.ArmorClass, CFBonusType.Item, 1, 60f);
            Assert.That(bonuses.Total(mob, CFBonusStat.ArmorClass), Is.EqualTo(1));

            Assert.That(SEntMan.System<InventorySystem>().TryUnequip(mob, "outerClothing", silent: true, force: true));
            Assert.That(bonuses.Total(mob, CFBonusStat.Saves), Is.EqualTo(0), "Taking the armor off removes its runes.");
        });
    }

    [Test]
    public async Task ShieldBlockDamagesTheShieldUntilItBreaks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var shields = SEntMan.System<CFShieldSystem>();
            var mob = Human();
            var item = SEntMan.SpawnEntity("CFSteelShield", _map.Local(0, 0));
            var shield = new Entity<CFShieldComponent>(item, SEntMan.GetComponent<CFShieldComponent>(item));

            Assert.That(shields.Block(mob, shield, 12), Is.EqualTo(7), "Hardness 5 stops 5 of 12.");
            Assert.That(shield.Comp.CurrentHp, Is.EqualTo(13));
            Assert.That(shield.Comp.Broken, Is.False);

            shields.Block(mob, shield, 12);
            Assert.That(shield.Comp.CurrentHp, Is.EqualTo(6));
            Assert.That(shield.Comp.Broken, "At or below Broken Threshold 10.");
        });
    }

    [Test]
    public void BulkThresholds()
    {
        Assert.That(CFBulk.IsEncumbered(5 * CFBulk.One + 9, strength: 0), Is.False, "5 Bulk and 9 light is still 5.");
        Assert.That(CFBulk.IsEncumbered(6 * CFBulk.One, strength: 0));
        Assert.That(CFBulk.IsEncumbered(6 * CFBulk.One, strength: 1), Is.False);
        Assert.That(CFBulk.IsOverMaximum(10 * CFBulk.One, strength: 0), Is.False);
        Assert.That(CFBulk.IsOverMaximum(11 * CFBulk.One, strength: 0));
        Assert.That(CFBulk.Format(CFBulk.Light), Is.EqualTo("L"));
        Assert.That(CFBulk.Format(0), Is.EqualTo("—"));
        Assert.That(CFBulk.Format(2 * CFBulk.One), Is.EqualTo("2"));
        Assert.That(CFBulk.FromItemSize("Large"), Is.EqualTo(CFBulk.One));
    }
}
