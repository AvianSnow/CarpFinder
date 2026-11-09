using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>The stance and flourish rules, through the same attempt and perform steps the action bar uses.</summary>
[TestFixture]
[TestOf(typeof(CFStanceSystem))]
public sealed class CFStanceTest : GameTest
{
    private const string Mountain = "CFActMountainStance";
    private const string Crane = "CFActCraneStance";
    private const string Tiger = "CFActTigerStance";
    private const string Dragon = "CFActDragonStance";

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFStanceTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 200
    currentHp: 200
    armorFromEquipment: true
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
      Dexterity: 3
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30

- type: entity
  parent: CFLeatherArmor
  id: CFStanceTestClothes
  components:
  - type: CFArmor
    category: Unarmored
    acBonus: 0
";

    private CFDevMap _map = default!;

    private CFStanceSystem Stances => SEntMan.System<CFStanceSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Hero()
    {
        return SEntMan.SpawnEntity("CFStanceTestHero", _map.Local(0, 0));
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool CanUse(EntityUid hero, EntityUid action)
    {
        var attempt = new ActionAttemptEvent(hero, null);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private bool Use(EntityUid hero, Entity<ActionComponent> action, BaseActionEvent? ev = null)
    {
        if (!CanUse(hero, action))
            return false;

        SEntMan.System<SharedActionsSystem>().PerformAction(hero, action, ev);
        return true;
    }

    private int FistDie(EntityUid hero)
    {
        return SEntMan.System<CFStrikeSystem>().UnarmedStats(hero).DamageDie;
    }

    private void Wear(EntityUid hero, string id)
    {
        var item = SEntMan.SpawnEntity(id, _map.Local(0, 0));
        SEntMan.System<InventorySystem>().TryEquip(hero, item, "outerClothing", silent: true, force: true);
    }

    [Test]
    public async Task MountainStanceChangesTheNumbersAndDismissingPutsThemBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int acBefore = 0, dieBefore = 0, acIn = 0, bonusIn = 0, speedIn = 0, dieIn = 0, acAfter = 0, bonusAfter = 0, speedAfter = 0, dieAfter = 0;
        bool forceful = false, toggledIn = false, toggledAfter = true, dismissed = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var action = Grant(hero, Mountain);
            SEntMan.System<CFArmorSystem>().RefreshArmorClass(hero);
            acBefore = combat.ArmorClass;
            dieBefore = FistDie(hero);

            Use(hero, action);
            acIn = combat.ArmorClass;
            bonusIn = Bonuses.Total(hero, CFBonusStat.ArmorClass);
            speedIn = Bonuses.Total(hero, CFBonusStat.Speed);
            dieIn = FistDie(hero);
            forceful = SEntMan.System<CFStrikeSystem>().UnarmedStats(hero).Traits.Forceful;
            toggledIn = action.Comp.Toggled;

            dismissed = Use(hero, action);
            acAfter = combat.ArmorClass;
            bonusAfter = Bonuses.Total(hero, CFBonusStat.ArmorClass);
            speedAfter = Bonuses.Total(hero, CFBonusStat.Speed);
            dieAfter = FistDie(hero);
            toggledAfter = action.Comp.Toggled;
        });

        Assert.Multiple(() =>
        {
            Assert.That(acIn, Is.EqualTo(acBefore - 3), "Dexterity 3 stops counting.");
            Assert.That(bonusIn, Is.EqualTo(4));
            Assert.That(speedIn, Is.EqualTo(-5));
            Assert.That(dieIn, Is.EqualTo(8));
            Assert.That(forceful, Is.True);
            Assert.That(toggledIn, Is.True, "The action bar shows it as on.");
            Assert.That(dismissed, Is.True, "Dismissing is not a stance action, so it does not have to wait.");
            Assert.That(acAfter, Is.EqualTo(acBefore));
            Assert.That(bonusAfter, Is.Zero);
            Assert.That(speedAfter, Is.Zero);
            Assert.That(dieAfter, Is.EqualTo(dieBefore));
            Assert.That(toggledAfter, Is.False);
        });
    }

    [Test]
    public async Task AnotherStanceHasToWaitARoundAndReplacesTheFirst()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        Entity<ActionComponent> crane = default;
        var tooSoon = true;
        var dieTooSoon = 0;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            var mountain = Grant(hero, Mountain);
            crane = Grant(hero, Crane);
            Use(hero, mountain);
            tooSoon = Use(hero, crane);
            dieTooSoon = FistDie(hero);
        });

        await Pair.RunSeconds(6.5f);

        var later = false;
        var bonus = 0;
        var speed = 0;
        var die = 0;
        await Server.WaitPost(() =>
        {
            later = Use(hero, crane);
            bonus = Bonuses.Total(hero, CFBonusStat.ArmorClass);
            speed = Bonuses.Total(hero, CFBonusStat.Speed);
            die = FistDie(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(tooSoon, Is.False);
            Assert.That(dieTooSoon, Is.EqualTo(8), "It is still in the first stance.");
            Assert.That(later, Is.True);
            Assert.That(bonus, Is.EqualTo(1), "Mountain's +4 went away with it.");
            Assert.That(speed, Is.Zero);
            Assert.That(die, Is.EqualTo(6));
        });
    }

    [Test]
    public async Task ArmorKeepsYouOutOfAStanceAndEndsOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var armored = true;
        var clothed = false;
        var inStance = false;
        var afterArmor = true;
        await Server.WaitPost(() =>
        {
            var wearer = SEntMan.SpawnEntity("CFStanceTestHero", _map.Local(1, 0));
            Wear(wearer, "CFLeatherArmor");
            armored = CanUse(wearer, Grant(wearer, Tiger));

            var dressed = SEntMan.SpawnEntity("CFStanceTestHero", _map.Local(1, 0));
            Wear(dressed, "CFStanceTestClothes");
            clothed = CanUse(dressed, Grant(dressed, Tiger));

            var hero = Hero();
            Use(hero, Grant(hero, Tiger));
            inStance = Stances.Current(hero) != null;
            Wear(hero, "CFLeatherArmor");
            afterArmor = Stances.Current(hero) != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(armored, Is.False);
            Assert.That(clothed, Is.True, "Clothing that counts as unarmored is fine.");
            Assert.That(inStance, Is.True);
            Assert.That(afterArmor, Is.False);
        });
    }

    [Test]
    public async Task BeingKnockedOutEndsAStance()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var before = false;
        var after = true;
        var bonus = 1;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            Use(hero, Grant(hero, Crane));
            before = Stances.Current(hero) != null;
            SEntMan.System<MobStateSystem>().ChangeMobState(hero, MobState.Critical);
            after = Stances.Current(hero) != null;
            bonus = Bonuses.Total(hero, CFBonusStat.ArmorClass);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.True);
            Assert.That(after, Is.False);
            Assert.That(bonus, Is.Zero);
        });
    }

    /// <summary>Strength 40 means a Strike that is allowed always lands, so no damage can only mean it was refused.</summary>
    [TestCase(Crane, false)]
    [TestCase(Tiger, true)]
    public async Task SomeStancesAllowOnlyTheirOwnAttack(string stance, bool weaponsAllowed)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var canSwing = !weaponsAllowed;
        var canPunch = false;
        var damaged = !weaponsAllowed;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var target = SEntMan.SpawnEntity("CFTrainingDummy", _map.Local(1, 0));
            var dagger = SEntMan.SpawnEntity("CFDagger", _map.Local(0, 0));
            var strikes = SEntMan.System<CFStrikeSystem>();
            var blocker = SEntMan.System<ActionBlockerSystem>();
            Use(hero, Grant(hero, stance));

            canSwing = blocker.CanAttack(hero, target, (dagger, SEntMan.GetComponent<MeleeWeaponComponent>(dagger)));
            canPunch = blocker.CanAttack(hero, target, (hero, SEntMan.GetComponent<MeleeWeaponComponent>(hero)));

            var combat = SEntMan.GetComponent<CFCombatantComponent>(target);
            var before = combat.CurrentHp;
            SEntMan.System<CFWeaponSystem>().TryStats(dagger, out var stats);
            strikes.PerformStrike(hero, target, stats, dagger, melee: true, countMap: false);
            damaged = combat.CurrentHp < before;
        });

        Assert.Multiple(() =>
        {
            Assert.That(canSwing, Is.EqualTo(weaponsAllowed));
            Assert.That(canPunch, Is.True, "The stance's own attack is still allowed.");
            Assert.That(damaged, Is.EqualTo(weaponsAllowed));
        });
    }

    [Test]
    public async Task PowerfulFistMakesAStanceAttackLethal()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var plain = false;
        var monk = true;
        var die = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var strikes = SEntMan.System<CFStrikeSystem>();
            Use(hero, Grant(hero, Dragon));
            plain = strikes.UnarmedStats(hero).Traits.Nonlethal;

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).PowerfulFist = true;
            monk = strikes.UnarmedStats(hero).Traits.Nonlethal;
            die = FistDie(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.True);
            Assert.That(monk, Is.False);
            Assert.That(die, Is.EqualTo(10), "The stance's own die is kept.");
        });
    }

    [Test]
    public async Task PointBlankIsAStance()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var inStance = false;
        var after = true;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var action = Grant(hero, "CFActPointBlank");
            Use(hero, action);
            inStance = Stances.Current(hero) is { PointBlank: true };
            Use(hero, action);
            after = Stances.Current(hero) != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(inStance, Is.True);
            Assert.That(after, Is.False);
        });
    }

    [Test]
    public async Task OneFlourishUntilThePoolIsFull()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        Entity<ActionComponent> charge = default;
        var first = false;
        var second = true;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            var target = SEntMan.SpawnEntity("CFTrainingDummy", _map.Local(1, 0));
            var flurry = Grant(hero, "CFActFlurry");
            charge = Grant(hero, "CFActSuddenCharge");
            first = Use(hero, flurry, new CFActivityTargetEvent { Target = target });
            second = CanUse(hero, charge);

            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            pool.Stamina = CFActionPoolSystem.MaxStamina(pool);
        });

        await Pair.RunTicksSync(3);

        var refreshed = false;
        await Server.WaitPost(() => refreshed = CanUse(hero, charge));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "Sudden Charge is a flourish too.");
            Assert.That(refreshed, Is.True, "A full pool is a new turn.");
        });
    }
}
