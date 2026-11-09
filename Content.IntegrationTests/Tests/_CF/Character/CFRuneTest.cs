using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Crafting;
using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Placeable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Item runes and etched runes at a workbench, and tracing and invoking a Runesmith's runes, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFRuneSystem))]
public sealed class CFRuneTest : GameTest
{
    private const int Extreme = 60;
    private static readonly Box2i Footprint = new(-2, -2, 4, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFRuneHero
  components:
  - type: CFCombatant
    level: 5
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFRuneHero
  id: CFRuneFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    armorClass: 1
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFRuneSystem Runes => SEntMan.System<CFRuneSystem>();

    private CFWorkbenchSystem Workbench => SEntMan.System<CFWorkbenchSystem>();

    private EntityUid Spawn(string id, int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Crafter(int level = 5)
    {
        var mob = Spawn("CFRuneHero", 2);
        SEntMan.GetComponent<CFAbilityScoreComponent>(mob).AbilityModifiers[CFAbility.Intelligence] = Extreme;
        var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
        combat.Level = level;
        combat.TrainedSkills.Add(CFCraftingRules.Skill);
        return mob;
    }

    private void Teach(EntityUid mob, string recipe)
    {
        EnsureBook(mob).Known.Add(recipe);
    }

    private CFFormulaBookComponent EnsureBook(EntityUid mob) => SEntMan.EnsureComponent<CFFormulaBookComponent>(mob);

    private CFRuneItemComponent Item(string rune)
    {
        var factory = SEntMan.ComponentFactory;
        Assert.That(Server.ProtoMan.Index<EntityPrototype>(rune).TryComp(out CFRuneItemComponent? item, factory), Is.True);
        return item!;
    }

    private async Task<Entity<CFWorkbenchComponent>> Bench(params string[] things)
    {
        Entity<CFWorkbenchComponent> station = default;
        var placed = new EntityUid[things.Length];
        await Server.WaitPost(() =>
        {
            var uid = Spawn("CFWorkbench");
            station = (uid, SEntMan.GetComponent<CFWorkbenchComponent>(uid));
            for (var i = 0; i < things.Length; i++)
            {
                placed[i] = Spawn(things[i]);
                SEntMan.System<SharedPhysicsSystem>().WakeBody(placed[i]);
            }
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            foreach (var thing in placed)
            {
                Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(station).PlacedEntities, Does.Contain(thing));
            }
        });
        return station;
    }

    private EntityUid First<T>(Entity<CFWorkbenchComponent> bench) where T : IComponent
    {
        return SEntMan.GetComponent<ItemPlacerComponent>(bench).PlacedEntities.First(SEntMan.HasComponent<T>);
    }

    private EntityUid Action(EntityUid hero, string id)
    {
        foreach (var action in SEntMan.System<SharedActionsSystem>().GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        Assert.Fail($"{id} was not granted");
        return default;
    }

    private void Target(EntityUid hero, EntityUid action, EntityUid target)
    {
        var ev = new CFActivityTargetEvent { Performer = hero, Target = target };
        SEntMan.EventBus.RaiseLocalEvent(action, ev);
    }

    private EntityUid Runesmith(params string[] choices)
    {
        var hero = Spawn("CFRuneHero");
        var features = SEntMan.System<CFCharacterFeatureSystem>();
        features.Apply(hero, Server.ProtoMan.Index<CFClassPrototype>("Runesmith").Features.First(f => f.Name == "Runes").Grants, "runes");
        foreach (var id in choices)
        {
            features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }

        return hero;
    }

    [Test]
    public async Task AWorkbenchAttachesARuneToTheWeaponOnIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var bench = await Bench("CFBattleAxe", "CFRuneItemWeaponPotency1");
        await Server.WaitAssertion(() =>
        {
            var hero = Crafter();
            Teach(hero, "AttachWeaponPotency1");
            var axe = First<CFWeaponComponent>(bench);
            var rune = First<CFRuneItemComponent>(bench);

            var degree = Workbench.TryCraft(bench, hero, Server.ProtoMan.Index<CFCraftingRecipePrototype>("AttachWeaponPotency1"));
            Assert.That(degree, Is.Not.Null);
            Assert.That(degree.Value.IsSuccess(), Is.True);
            Assert.That(SEntMan.GetComponent<CFWeaponRunesComponent>(axe).Potency, Is.EqualTo(1));
            Assert.That(SEntMan.Deleted(rune) || SEntMan.IsQueuedForDeletion(rune), Is.True);
            Assert.That(SEntMan.Deleted(axe), Is.False);
            Assert.That(SEntMan.System<CFWeaponSystem>().TryStats(axe, out var stats) && stats.Potency == 1, Is.True);
        });
    }

    [Test]
    public async Task TheRecipeNeedsSomethingToPutTheRuneOn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var bench = await Bench("CFRuneItemWeaponPotency1");
        await Server.WaitAssertion(() =>
        {
            var hero = Crafter();
            Teach(hero, "AttachWeaponPotency1");
            var entry = Workbench.Entries(bench, hero).Single(e => e.Recipe == "AttachWeaponPotency1");
            Assert.That(entry.HasMaterials, Is.False);
            Assert.That(Workbench.TryCraft(bench, hero, Server.ProtoMan.Index<CFCraftingRecipePrototype>("AttachWeaponPotency1")), Is.Null);
        });
    }

    [Test]
    public async Task RuneRecipesFollowTheLevelRule()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var low = Crafter(level: 1);
            var potency = Server.ProtoMan.Index<CFCraftingRecipePrototype>("AttachWeaponPotency1");
            Assert.That(Workbench.BlockedReason(low, potency), Is.EqualTo("cf-craft-blocked-level"));

            var capped = Crafter(level: 5);
            Assert.That(Workbench.BlockedReason(capped, potency), Is.Null);
            Assert.That(Workbench.BlockedReason(capped, Server.ProtoMan.Index<CFCraftingRecipePrototype>("AttachFlaming")), Is.EqualTo("cf-craft-blocked-level"));
        });
    }

    [Test]
    public async Task OnlyARunesmithWhoKnowsItCanEtchARune()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var etch = Server.ProtoMan.Index<CFCraftingRecipePrototype>("EtchEsvadir");
            var stranger = Crafter();
            Assert.That(Workbench.BlockedReason(stranger, etch), Is.EqualTo("cf-rune-unknown"));

            var smith = Runesmith("Rune1Esvadir");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(smith);
            combat.TrainedSkills.Add(CFCraftingRules.Skill);
            Assert.That(Workbench.BlockedReason(smith, etch), Is.Null);
            Assert.That(SEntMan.GetComponent<CFFormulaBookComponent>(smith).Known, Does.Contain(new ProtoId<CFCraftingRecipePrototype>("EtchEsvadir")));
        });
    }

    [Test]
    public async Task APropertyRuneNeedsAPotencySlot()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var axe = Spawn("CFBattleAxe");
            var flaming = Item("CFRuneItemFlaming");
            Assert.That(Runes.CanAttach(axe, flaming), Is.False);

            var runes = SEntMan.EnsureComponent<CFWeaponRunesComponent>(axe);
            runes.Potency = 1;
            Assert.That(Runes.CanAttach(axe, flaming), Is.True);

            runes.Property.Add("Flaming");
            Assert.That(Runes.CanAttach(axe, flaming), Is.False);

            // A rune for armor does not go on a weapon.
            Assert.That(Runes.CanAttach(axe, Item("CFRuneItemArmorPotency1")), Is.False);
        });
    }

    [Test]
    public async Task EtchingPastTheLimitFadesTheOldest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Crafter(level: 1);
            var axes = Enumerable.Range(0, 3).Select(_ => Spawn("CFBattleAxe")).ToArray();
            foreach (var axe in axes)
            {
                Runes.Etch(axe, "Esvadir", hero);
            }

            Assert.That(SEntMan.GetComponent<CFRuneBearerComponent>(axes[0]).Runes, Is.Empty);
            Assert.That(SEntMan.GetComponent<CFRuneBearerComponent>(axes[2]).Runes, Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<CFRunesmithComponent>(hero).Etched, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task AnEtchedWeaponRuneWorksOnEveryStrike()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFRuneHero");
            var axe = Spawn("CFBattleAxe");
            Runes.Etch(axe, "Esvadir", hero);
            var foe = Spawn("CFRuneFoe", 1);
            Assert.That(SEntMan.System<CFWeaponSystem>().TryStats(axe, out var stats), Is.True);
            var strikes = SEntMan.System<CFStrikeSystem>();
            for (var i = 0; i < 40 && !SEntMan.HasComponent<CFPersistentDamageComponent>(foe); i++)
            {
                strikes.PerformStrike(hero, foe, stats, axe, melee: true, countMap: false);
            }

            Assert.That(SEntMan.HasComponent<CFPersistentDamageComponent>(foe), Is.True);
        });
    }

    [Test]
    public async Task ATracedRuneGivesItsBonusAndFadesOnInvoke()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Runesmith("Rune1Lyskel");
            var foe = Spawn("CFRuneFoe", 1);
            var bonuses = SEntMan.System<CFBonusSystem>();

            Target(hero, Action(hero, "CFActTraceLyskel"), foe);
            Assert.That(bonuses.Total(foe, CFBonusStat.Speed), Is.EqualTo(-5));
            Assert.That(SEntMan.GetComponent<CFRuneBearerComponent>(foe).Runes, Has.Count.EqualTo(1));

            Target(hero, Action(hero, "CFActInvokeRune"), foe);
            Assert.That(SEntMan.GetComponent<CFRuneBearerComponent>(foe).Runes, Is.Empty);
            Assert.That(bonuses.Total(foe, CFBonusStat.Speed), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task InvokingARuneHurtsItsBearer()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Runesmith("Rune1Atryl");
            var foe = Spawn("CFRuneFoe", 1);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(foe);
            for (var i = 0; i < 30 && combat.CurrentHp == 500; i++)
            {
                Target(hero, Action(hero, "CFActTraceAtryl"), foe);
                Target(hero, Action(hero, "CFActInvokeRune"), foe);
            }

            Assert.That(combat.CurrentHp, Is.LessThan(500));
        });
    }

    [Test]
    public async Task ATracedRuneFadesByItself()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid foe = default;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Lyskel");
            foe = Spawn("CFRuneFoe", 1);
            Target(hero, Action(hero, "CFActTraceLyskel"), foe);
        });
        await Pair.RunSeconds(13);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CFRuneBearerComponent>(foe).Runes, Is.Empty);
            Assert.That(SEntMan.System<CFBonusSystem>().Total(foe, CFBonusStat.Speed), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AWeaponRuneRefusesTheWrongWeapon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            // Marssyl is for bludgeoning weapons, and a battle axe slashes.
            Assert.That(Runes.CanAttach(Spawn("CFBattleAxe"), Item("CFRuneEtchMarssyl")), Is.False);
            Assert.That(Runes.CanAttach(Spawn("CFWarhammer"), Item("CFRuneEtchMarssyl")), Is.True);
            Assert.That(Runes.CanAttach(Spawn("CFSteelShield"), Item("CFRuneEtchHoltrik")), Is.True);
        });
    }

    [Test]
    public async Task EveryRuneCanBeTakenAndEveryRecipeResolves()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var runes = 0;
        await Server.WaitPost(() =>
        {
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Runesmith"))
            {
                features.Apply(Spawn("CFRuneHero"), choice.Grants, choice.ID);
                runes++;
            }

            foreach (var recipe in Server.ProtoMan.EnumeratePrototypes<CFCraftingRecipePrototype>().Where(r => r.AttachRune))
            {
                Assert.That(Server.ProtoMan.Index<EntityPrototype>(recipe.Result).TryComp<CFRuneItemComponent>(out _, SEntMan.ComponentFactory), Is.True, recipe.ID);
            }

            foreach (var rune in Server.ProtoMan.EnumeratePrototypes<CFRunePrototype>())
            {
                Assert.That(Server.ProtoMan.HasIndex<EntityPrototype>(rune.Action), Is.True, rune.ID);
            }
        });

        Assert.That(runes, Is.EqualTo(6 * Server.ProtoMan.EnumeratePrototypes<CFRunePrototype>().Count()));
    }
}
