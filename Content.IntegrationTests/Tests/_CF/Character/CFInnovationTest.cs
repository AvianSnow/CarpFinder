using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>An Inventor's innovation, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFInventorSystem))]
public sealed class CFInnovationTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFInnovationHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
    abilityModifiers:
      Intelligence: 40
  - type: CFCombatOptions
  - type: CFExperience
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFInnovationHero
  id: CFInnovationFoe
  components:
  - type: CFFaction
    faction: Monster

# Nothing but acid gets through to it.
- type: entity
  parent: CFInnovationFoe
  id: CFInnovationFireproofFoe
  components:
  - type: CFResistances
    entries:
    - damageType: Fire
      amount: 999
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFInventorSystem Inventor => SEntMan.System<CFInventorSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Spawn(string id = "CFInnovationHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private void GiveFeature(EntityUid hero, string name)
    {
        var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Inventor")).Features.First(f => f.Name == name);
        Features.Apply(hero, feature.Grants, name);
    }

    private void Choose(EntityUid hero, string id) => Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    /// <summary>Answers an option's pick, as the builder would, so a grant that reads it finds it.</summary>
    private void Pick(EntityUid hero, string source, string value, int slot = 0)
    {
        SEntMan.EnsureComponent<CFExperienceComponent>(hero).OptionPicks.Add(new CFOptionPick { Source = source, Slot = slot, Value = value });
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool Allowed(EntityUid hero, Entity<ActionComponent> action, EntityUid? target = null)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private void Wear(EntityUid hero, string id)
    {
        Assert.That(SEntMan.System<InventorySystem>().TryEquip(hero, Spawn(id), "outerClothing", force: true), Is.True);
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    /// <summary>The weapon innovation an Inventor was given: at their feet, as the test hero has no backpack.</summary>
    private EntityUid? WeaponInnovationOf(EntityUid hero)
    {
        var query = SEntMan.EntityQueryEnumerator<CFInnovationComponent, CFWeaponComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (SEntMan.GetComponent<TransformComponent>(uid).Coordinates.TryDistance(SEntMan, SEntMan.GetComponent<TransformComponent>(hero).Coordinates, out var distance) && distance < 1f)
                return uid;
        }

        return null;
    }

    /// <summary>A build with the construct innovation, so the spawn system spawns one, and the construct it made.</summary>
    private EntityUid SpawnConstruct(EntityUid hero, int level = 1, params string[] choices)
    {
        var build = new CFCharacterBuildData { Class = "Inventor", Level = level };
        build.ClassChoices.Add("InnovationConstruct");
        foreach (var choice in choices)
        {
            build.ClassChoices.Add(choice);
            Choose(hero, choice);
        }

        SEntMan.EnsureComponent<CFExperienceComponent>(hero).Build = build;
        SEntMan.System<CFCharacterSpawnSystem>().EnsureCompanion(hero, build, level);
        var construct = Inventor.Construct(hero);
        Assert.That(construct, Is.Not.Null, "The construct spawned.");
        return construct!.Value;
    }

    /// <summary>Intelligence 40 against a low DC means success, or a critical success, all but always.</summary>
    [Test]
    public async Task OverdriveAddsDamageAndThenWaits()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = -1, during = 0;
        var again = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Overdrive");
            before = Inventor.OverdriveDamage(hero);
            var action = Action(hero, "CFActOverdrive");
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            during = Inventor.OverdriveDamage(hero);
            SEntMan.GetComponent<CFOverdriveComponent>(hero).Until = System.TimeSpan.Zero;
            again = Allowed(hero, action);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(during, Is.AnyOf(20, 40));
            Assert.That(again, Is.False, "It has to cool down.");
        });
    }

    [Test]
    public async Task ExplodeNeedsTheInnovationAndHurtsEnemiesNearIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool without = true, with = false;
        var most = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Explode");
            var action = Action(hero, "CFActExplode");
            without = Allowed(hero, action);

            Wear(hero, "CFInnovationPowerSuit");
            with = Allowed(hero, action);

            var foe = Spawn("CFInnovationFoe", 1);
            for (var i = 0; i < 30; i++)
            {
                SEntMan.EnsureComponent<CFOverdriveComponent>(hero).UnstableDownUntil = System.TimeSpan.Zero;
                Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            }

            most = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.False);
            Assert.That(with, Is.True);
            Assert.That(most, Is.GreaterThan(0));
        });
    }

    /// <summary>Variable Core: a foe that shrugs off fire is still burnt by an acid core.</summary>
    [Test]
    public async Task VariableCoreChangesWhatExplodeDealsWith()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var fire = 0;
        var acid = 0;
        string core = "";
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Explode");
            Wear(hero, "CFInnovationPowerSuit");
            var action = Action(hero, "CFActExplode");
            var foe = Spawn("CFInnovationFireproofFoe", 1);
            for (var i = 0; i < 20; i++)
            {
                SEntMan.EnsureComponent<CFOverdriveComponent>(hero).UnstableDownUntil = System.TimeSpan.Zero;
                Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            }

            fire = Hurt(foe);

            Pick(hero, "CFVariableCore", "Acid");
            GiveFeat(hero, "CFVariableCore");
            core = Inventor.CoreType(hero);
            for (var i = 0; i < 20; i++)
            {
                SEntMan.EnsureComponent<CFOverdriveComponent>(hero).UnstableDownUntil = System.TimeSpan.Zero;
                Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            }

            acid = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(fire, Is.EqualTo(0), "Fire did nothing to it.");
            Assert.That(core, Is.EqualTo("Acid"));
            Assert.That(acid, Is.GreaterThan(0), "Acid got through.");
        });
    }

    [Test]
    public async Task SearingRestorationNeedsTheInnovationAndHeals()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool without = true, with = false;
        int least = int.MaxValue, most = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFSearingRestoration");
            var action = Action(hero, "CFActSearingRestoration");
            without = Allowed(hero, action, hero);

            Wear(hero, "CFInnovationPowerSuit");
            with = Allowed(hero, action, hero);

            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var effects = SEntMan.System<CFEffectSystem>();
            for (var i = 0; i < 60; i++)
            {
                combat.CurrentHp = 100;
                effects.ResolveTarget(action, hero, hero);
                least = System.Math.Min(least, combat.CurrentHp - 100);
                most = System.Math.Max(most, combat.CurrentHp - 100);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.False);
            Assert.That(with, Is.True);
            Assert.That(least, Is.GreaterThanOrEqualTo(1));
            Assert.That(most, Is.InRange(6, 10), "1d10 at level 1.");
        });
    }

    [Test]
    public async Task AFailedUnstableCheckStopsTheInnovation()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var stopped = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Wear(hero, "CFInnovationPowerSuit");
            for (var i = 0; i < 200 && !stopped; i++)
            {
                Inventor.RollUnstable(hero);
                stopped = !Inventor.CanUseUnstable(hero);
            }
        });

        Assert.That(stopped, Is.True);
    }

    [Test]
    public async Task ArmorResistanceCountsOnlyWhileWorn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int bare = 0, worn = 0, other = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var bonuses = SEntMan.System<CFBonusSystem>();
            Choose(hero, "ModArmorHarmonicOscillator");
            bare = bonuses.ModifyDamage(hero, "Sonic", 10);
            Wear(hero, "CFInnovationPowerSuit");
            worn = bonuses.ModifyDamage(hero, "Sonic", 10);
            other = bonuses.ModifyDamage(hero, "Fire", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(bare, Is.EqualTo(10));
            Assert.That(worn, Is.EqualTo(7), "3 + half of level 1, rounded down.");
            Assert.That(other, Is.EqualTo(10));
        });
    }

    /// <summary>Collapse Armor: the suit comes off into a hand, and goes back on from it.</summary>
    [Test]
    public async Task CollapseArmorTakesTheSuitOffAndPutsItBackOn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool wornFirst = false, heldAfter = false, wornAgain = false, allowedBare = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFCollapseArmor");
            var action = Action(hero, "CFActCollapseArmor");
            allowedBare = Allowed(hero, action);

            Wear(hero, "CFInnovationPowerSuit");
            wornFirst = Inventor.IsWearing(hero);
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            heldAfter = !Inventor.IsWearing(hero) && Hands.EnumerateHeld(hero).Any(SEntMan.HasComponent<CFInnovationComponent>);
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            wornAgain = Inventor.IsWearing(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(allowedBare, Is.False, "Nothing to collapse.");
            Assert.That(wornFirst, Is.True);
            Assert.That(heldAfter, Is.True, "Off and in the hand.");
            Assert.That(wornAgain, Is.True, "Back on.");
        });
    }

    /// <summary>A weapon innovation is the picked base with the modification's traits, named after it.</summary>
    [Test]
    public async Task AWeaponInnovationIsBuiltOnThePickedBase()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string proto = "", name = "";
        var traits = new List<string>();
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Pick(hero, "ModWeaponEntanglingForm", "pick:CFRapier");
            Choose(hero, "ModWeaponEntanglingForm");
            var weapon = WeaponInnovationOf(hero);
            Assert.That(weapon, Is.Not.Null, "The weapon was made.");
            proto = SEntMan.GetComponent<MetaDataComponent>(weapon!.Value).EntityPrototype!.ID;
            name = SEntMan.GetComponent<MetaDataComponent>(weapon.Value).EntityName;
            traits.AddRange(SEntMan.GetComponent<CFTraitComponent>(weapon.Value).Traits.Select(t => t.Id));
        });

        Assert.Multiple(() =>
        {
            Assert.That(proto, Is.EqualTo("CFRapier"));
            Assert.That(name, Is.EqualTo("rapier innovation"));
            Assert.That(traits, Is.SupersetOf(new[] { "Disarm", "Grapple", "Trip", "Finesse" }), "The modification's traits on top of the rapier's own.");
        });
    }

    /// <summary>With no pick the base is a longsword, as the old innovations were.</summary>
    [Test]
    public async Task ComplexSimplicityStepsTheDieAndAddsThePickedTraits()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var die = 0;
        var traits = new List<string>();
        var modes = 0;
        var longsword = "";
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Pick(hero, "ModWeaponComplexSimplicity", "pick:CFDagger");
            Pick(hero, "ModWeaponComplexSimplicity", "pick:Razing", 1);
            Pick(hero, "ModWeaponComplexSimplicity", "pick:VersatileB", 2);
            Choose(hero, "ModWeaponComplexSimplicity");
            var weapon = WeaponInnovationOf(hero)!.Value;
            die = SEntMan.GetComponent<CFWeaponComponent>(weapon).DamageDie;
            traits.AddRange(SEntMan.GetComponent<CFTraitComponent>(weapon).Traits.Select(t => t.Id));
            SEntMan.DeleteEntity(weapon);

            var other = Spawn(x: 2);
            Choose(other, "ModWeaponModularHead");
            var modular = WeaponInnovationOf(other)!.Value;
            longsword = SEntMan.GetComponent<MetaDataComponent>(modular).EntityPrototype!.ID;
            modes = SEntMan.GetComponent<CFDamageModeComponent>(modular).Types.Count;
        });

        Assert.Multiple(() =>
        {
            Assert.That(die, Is.EqualTo(6), "A dagger's d4, one step up.");
            Assert.That(traits, Is.SupersetOf(new[] { "Razing", "VersatileB" }));
            Assert.That(longsword, Is.EqualTo("CFLongsword"));
            Assert.That(modes, Is.EqualTo(3), "Bludgeoning, piercing and slashing.");
        });
    }

    /// <summary>Hampering, razing and tearing are rules now, read off the traits.</summary>
    [Test]
    public async Task HamperingRazingAndTearingAreRules()
    {
        await Server.WaitAssertion(() =>
        {
            var traits = CFWeaponTraits.From(Server.ProtoMan, new[] { new ProtoId<CFTraitPrototype>("Hampering"), new("Razing"), new("Tearing") });
            Assert.That(traits.Hampering, Is.True);
            Assert.That(traits.Razing, Is.True);
            Assert.That(traits.Tearing, Is.True);
        });
    }

    /// <summary>A hampering weapon's hit slows the target, and a tearing one makes it bleed.</summary>
    [Test]
    public async Task AHamperingHitSlowsAndATearingHitBleeds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var slowed = false;
        var bled = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Pick(hero, "ModWeaponHamperingSpikes", "pick:CFLongsword");
            Choose(hero, "ModWeaponHamperingSpikes");
            var weapon = WeaponInnovationOf(hero)!.Value;
            var traits = SEntMan.GetComponent<CFTraitComponent>(weapon);
            traits.Traits.Add("Tearing");
            Hands.TryPickupAnyHand(hero, weapon, animate: false);
            var foe = Spawn("CFInnovationFoe", 1);
            var strikes = SEntMan.System<CFStrikeSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();
            for (var i = 0; i < 40 && !(slowed && bled); i++)
            {
                Assert.That(strikes.TryMeleeWeapon(hero, out var stats, out var held), Is.True);
                strikes.PerformStrike(hero, foe, stats, held, melee: true, countMap: false, fixedMap: 0);
                slowed |= bonuses.Total(foe, CFBonusStat.Speed) <= -10;
                bled |= SEntMan.HasComponent<CFPersistentDamageComponent>(foe);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(slowed, Is.True, "-10 feet after a hit.");
            Assert.That(bled, Is.True, "Persistent bleed after a hit.");
        });
    }

    /// <summary>Tamper: the held weapon, else the armor.</summary>
    [Test]
    public async Task TamperPenalizesTheWeaponOrTheArmor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int attack = 0, speed = 0;
        bool offGuard = false, armedAllowed = false, bareAllowed = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFTamper");
            var action = Action(hero, "CFActTamper");
            var effects = SEntMan.System<CFEffectSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();

            var bare = Spawn("CFInnovationFoe", 1);
            bareAllowed = Allowed(hero, action, bare);

            var armed = Spawn("CFInnovationFoe", 2);
            Hands.TryPickupAnyHand(armed, Spawn("CFLongsword", 2), animate: false);
            armedAllowed = Allowed(hero, action, armed);
            effects.ResolveTarget(action, hero, armed);
            attack = bonuses.Total(armed, CFBonusStat.AttackRoll);

            var armored = Spawn("CFInnovationFoe", 3);
            Wear(armored, "CFBreastplate");
            effects.ResolveTarget(action, hero, armored);
            speed = bonuses.Total(armored, CFBonusStat.Speed);
            offGuard = SEntMan.System<CFConditionSystem>().IsOffGuardTo(armored, hero, melee: true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(bareAllowed, Is.False, "Nothing to tamper with.");
            Assert.That(armedAllowed, Is.True);
            Assert.That(attack, Is.EqualTo(-2), "Crafting 40 against Reflex: it lands.");
            Assert.That(speed, Is.EqualTo(-10));
            Assert.That(offGuard, Is.True);
        });
    }

    /// <summary>A modification picked for one innovation goes when the innovation is another.</summary>
    [Test]
    public async Task AModificationNeedsItsInnovation()
    {
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData { Class = "Inventor" };
            build.ClassChoices.Add("InnovationWeapon");
            build.ClassChoices.Add("ModArmorSpeedBoosters");
            Assert.That(CFClassChoices.Selected(Server.ProtoMan, build).Select(c => c.ID), Is.EquivalentTo(new[] { "InnovationWeapon" }));

            build.ClassChoices.Clear();
            build.ClassChoices.Add("ModArmorMuscularExoskeleton");
            build.ClassChoices.Add("InnovationArmor");
            Assert.That(CFClassChoices.Selected(Server.ProtoMan, build).Select(c => c.ID), Is.EquivalentTo(new[] { "InnovationArmor", "ModArmorMuscularExoskeleton" }), "Whichever was picked first.");

            build.ClassChoices.Clear();
            build.ClassChoices.Add("InnovationArmorSubterfuge");
            build.ClassChoices.Add("ModArmorMuscularExoskeleton");
            Assert.That(CFClassChoices.Selected(Server.ProtoMan, build).Select(c => c.ID), Is.EquivalentTo(new[] { "InnovationArmorSubterfuge" }), "The exoskeleton is for the power suit.");
        });
    }

    /// <summary>The construct takes its modifications as it spawns.</summary>
    [Test]
    public async Task TheConstructTakesItsModifications()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var speed = 0;
        var stealth = CFProficiencyRank.Untrained;
        var dark = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var construct = SpawnConstruct(hero, 1, "ModConstructAcceleratedMobility", "ModConstructWonderGears", "ModConstructSensoryArray");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(construct);
            speed = combat.SpeedFeet;
            stealth = combat.ProficiencyRanks.TryGetValue("Stealth", out var rank) ? rank : CFProficiencyRank.Untrained;
            dark = SEntMan.GetComponent<CFSensesComponent>(construct).Darkvision;
        });

        Assert.Multiple(() =>
        {
            Assert.That(speed, Is.EqualTo(40));
            Assert.That(stealth, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(dark, Is.True);
        });
    }

    /// <summary>The construct shares its Inventor's Overdrive.</summary>
    [Test]
    public async Task TheConstructSharesOverdrive()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var bonus = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Overdrive");
            var construct = SpawnConstruct(hero);
            Actions.PerformAction(hero, Action(hero, "CFActOverdrive"), new CFActivityInstantEvent());
            bonus = Inventor.OverdriveDamage(construct);
        });

        Assert.That(bonus, Is.AnyOf(20, 40));
    }

    /// <summary>Haphazard Repair mends the construct beside the Inventor, as Repair would.</summary>
    [Test]
    public async Task HaphazardRepairMendsTheConstruct()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var allowedAlone = true;
        var mended = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Expert Overdrive");
            GiveFeat(hero, "CFHaphazardRepair");
            var action = Action(hero, "CFActHaphazardRepair");
            allowedAlone = Allowed(hero, action);

            var construct = SpawnConstruct(hero);
            var body = SEntMan.GetComponent<CFCombatantComponent>(construct);
            body.MaxHp = 100;
            body.CurrentHp = 1;
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            mended = body.CurrentHp - 1;
        });

        Assert.Multiple(() =>
        {
            Assert.That(allowedAlone, Is.False, "No construct, nothing to repair.");
            Assert.That(mended, Is.AnyOf(20, 30), "10 + 5 per rank at expert, or 10 per rank on a critical success.");
        });
    }

    /// <summary>Collapse Construct folds the construct into a case and unfolds it with the Hit Points it had.</summary>
    [Test]
    public async Task CollapseConstructFoldsAndUnfolds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool folded = false, holdsCase = false, unfolded = false;
        var hpBack = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFCollapseConstruct");
            var action = Action(hero, "CFActCollapseConstruct");
            var construct = SpawnConstruct(hero);
            SEntMan.GetComponent<CFCombatantComponent>(construct).CurrentHp = 7;

            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            SEntMan.DeleteEntity(construct);
            folded = Inventor.Construct(hero) == null;
            holdsCase = Hands.EnumerateHeld(hero).Any(SEntMan.HasComponent<CFCollapsedConstructComponent>);

            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            unfolded = Inventor.Construct(hero) is { } back && (hpBack = SEntMan.GetComponent<CFCombatantComponent>(back).CurrentHp) > 0;
        });

        Assert.Multiple(() =>
        {
            Assert.That(folded, Is.True);
            Assert.That(holdsCase, Is.True);
            Assert.That(unfolded, Is.True);
            Assert.That(hpBack, Is.EqualTo(7), "Back with what it had.");
        });
    }

    /// <summary>Oil Fire needs a foe in the Inventor's grip.</summary>
    [Test]
    public async Task OilFireNeedsAGrabbedFoe()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool loose = true, grabbed = false, pulled = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Wear(hero, "CFInnovationPowerSuit");
            GiveFeat(hero, "CFOilFire");
            var action = Action(hero, "CFActOilFire");
            loose = Allowed(hero, action);

            // A friend: a pull on one is stock's free pull, with no Grapple roll to fail. The grab is set by hand.
            var held = Spawn(x: 1);
            pulled = SEntMan.System<PullingSystem>().TryStartPull(hero, held);
            SEntMan.System<CFConditionSystem>().ApplyGrabbed(held, hero);
            grabbed = Allowed(hero, action);
        });

        Assert.Multiple(() =>
        {
            Assert.That(loose, Is.False);
            Assert.That(pulled, Is.True, "The pull started.");
            Assert.That(grabbed, Is.True);
        });
    }

    /// <summary>Reverse Engineer: Crafting when it beats Thievery.</summary>
    [Test]
    public async Task ReverseEngineerUsesCraftingWhenItIsBetter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var before = "";
        var after = "";
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            before = Inventor.ThieverySkill(hero).Skill;
            GiveFeat(hero, "CFReverseEngineer");
            after = Inventor.ThieverySkill(hero).Skill;
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("Thievery"));
            Assert.That(after, Is.EqualTo("Crafting"), "Intelligence 40 makes Crafting the better one.");
        });
    }

    /// <summary>Built-In Tools: the innovation in use is the toolkit.</summary>
    [Test]
    public async Task BuiltInToolsAreOnTheInnovation()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool bare = true, worn = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFBuiltInTools");
            bare = Inventor.BuiltInToolkit(hero) != null;
            Wear(hero, "CFInnovationPowerSuit");
            worn = Inventor.BuiltInToolkit(hero) != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(bare, Is.False, "No innovation in use, no tools.");
            Assert.That(worn, Is.True);
        });
    }

    /// <summary>Dual-Form Weapon: Switch Configuration swaps the held innovation for its other base.</summary>
    [Test]
    public async Task SwitchConfigurationTurnsTheWeaponIntoItsOtherForm()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var first = "";
        var second = "";
        var traitsKept = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Pick(hero, "ModWeaponEntanglingForm", "pick:CFLongsword");
            Choose(hero, "ModWeaponEntanglingForm");
            Hands.TryPickupAnyHand(hero, WeaponInnovationOf(hero)!.Value, animate: false);
            Pick(hero, "CFDualFormWeapon", "pick:CFWarhammer");
            GiveFeat(hero, "CFDualFormWeapon");

            first = SEntMan.GetComponent<MetaDataComponent>(Inventor.HeldInnovation(hero)!.Value).EntityPrototype!.ID;
            Actions.PerformAction(hero, Action(hero, "CFActSwitchConfiguration"), new CFActivityInstantEvent());
            var held = Inventor.HeldInnovation(hero)!.Value;
            second = SEntMan.GetComponent<MetaDataComponent>(held).EntityPrototype!.ID;
            traitsKept = SEntMan.GetComponent<CFTraitComponent>(held).Traits.Contains("Grapple");
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("CFLongsword"));
            Assert.That(second, Is.EqualTo("CFWarhammer"));
            Assert.That(traitsKept, Is.True, "The modification goes with it.");
        });
    }

    [Test]
    public async Task EveryOptionCanBeAppliedAndEveryItemIsAnInnovation()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Inventor"))
            {
                Features.Apply(Spawn(), choice.Grants, choice.ID);
                count++;
            }

            foreach (var id in new[] { "CFInnovationPowerSuit", "CFInnovationSubterfugeSuit", "CFCompanionConstruct" })
            {
                Assert.That(SEntMan.HasComponent<CFInnovationComponent>(Spawn(id)), Is.True, id);
            }
        });

        Assert.That(count, Is.EqualTo(28), "4 innovations, 7 armor, 10 weapon and 7 construct modifications.");
    }

    [Test]
    public async Task TheConstructHasThePrototypeStatistics()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var pet = Server.ProtoMan.Index(new ProtoId<CFPetPrototype>("CFPetConstruct"));
            var block = CFPetMath.Present(pet, new CFCharacterBuildData { Class = "Inventor" }, null);
            Assert.That(block.ArmorClass, Is.EqualTo(16), "10 + level 1 + Dexterity 3 + trained 2.");
            Assert.That(block.HitPoints, Is.EqualTo(18), "10 + 6 + Constitution 2, at level 1.");

            var advanced = CFPetMath.Present(pet, new CFCharacterBuildData { Class = "Inventor", Level = 4, ClassFeatLevel4 = "CFAdvancedConstructCompanion" }, null);
            Assert.That(advanced.ArmorClass, Is.EqualTo(20), "10 + level 4 + Dexterity 4 + trained 2.");
            Assert.That(advanced.HitPoints, Is.EqualTo(46), "10 + (6 + Constitution 3) per level, at level 4.");
            Assert.That(advanced.Strikes[0].Damage, Does.StartWith("2d8"), "Two dice once advanced.");
            Assert.That(advanced.Fortitude, Is.EqualTo(11), "Level 4 + Constitution 3 + expert 4.");
        });
    }

    [Test]
    public async Task TheConstructIsNotAliveAndRollsLikeAPlayer()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var construct = Spawn("CFCompanionConstruct");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(construct);
            var effects = SEntMan.System<CFEffectSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();

            combat.CurrentHp = 5;
            effects.Heal(construct, construct, 10);
            Assert.That(combat.CurrentHp, Is.EqualTo(5), "Healing does not work on it.");
            Assert.That(bonuses.ModifyDamage(construct, "Poison", 50), Is.EqualTo(0));
            Assert.That(bonuses.ModifyDamage(construct, "Slashing", 50), Is.EqualTo(50));
            Assert.That(combat.ProficiencyRanks["Athletics"], Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(SEntMan.GetComponent<CFAbilityScoreComponent>(construct).GetModifier(CFAbility.Dexterity), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AConstructInnovationIsAConstructCompanion()
    {
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData { Class = "Inventor" };
            Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, build, CFPetKind.Construct), Is.False);
            build.ClassChoices.Add("InnovationConstruct");
            Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, build, CFPetKind.Construct), Is.True);
        });
    }

    /// <summary>A feat says which pet it gives with a grant, so the Pet tab and the spawn agree on it.</summary>
    [Test]
    public async Task AFeatGivesThePetItsGrantNames()
    {
        await Server.WaitAssertion(() =>
        {
            var inventor = new CFCharacterBuildData { Class = "Inventor", ClassFeat = "CFPrototypeCompanion" };
            var wizard = new CFCharacterBuildData { Class = "Wizard", ClassFeat = "CFFamiliarWizard" };
            var later = new CFCharacterBuildData { Class = "Wizard", ClassFeatLevel2 = "CFFamiliarWizard" };
            Assert.Multiple(() =>
            {
                Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, inventor, CFPetKind.Construct), Is.True);
                Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, inventor, CFPetKind.Familiar), Is.False);
                Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, wizard, CFPetKind.Familiar), Is.True);
                Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, wizard, CFPetKind.Companion), Is.False);
                Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, later, CFPetKind.Familiar), Is.False, "A level 2 feat waits for level 2.");
            });
        });
    }
}
