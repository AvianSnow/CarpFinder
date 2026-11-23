using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Runesmith's feats and the rune invocations that came with them, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFRuneSystem))]
public sealed class CFRunesmithTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFRunesmithHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    classId: Runesmith
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
      Dexterity: 40
      Constitution: 0
      Intelligence: 40
      Wisdom: 0
      Charisma: 0
  - type: CFCombatOptions
  - type: CFExperience
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFRunesmithHero
  id: CFRunesmithFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    armorClass: 1
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: -40
      Charisma: 0
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFRuneSystem Runes => SEntMan.System<CFRuneSystem>();

    private EntityUid Spawn(string id = "CFRunesmithHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private EntityUid Runesmith(params string[] choices)
    {
        var hero = Spawn();
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Runesmith")).Features.First(f => f.Name == "Runes").Grants, "runes");
        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }

        return hero;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private int RunesOn(EntityUid bearer) => SEntMan.GetComponentOrNull<CFRuneBearerComponent>(bearer)?.Runes.Count ?? 0;

    /// <summary>Engraving Strike traces the chosen rune on a hit, and a rune the Runesmith does not know traces nothing.</summary>
    [Test]
    public async Task EngravingStrikeTracesTheChosenRune()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int known = 0, unknown = 0;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Atryl");
            GiveFeat(hero, "CFEngravingStrike");
            var action = Action(hero, "CFActEngravingStrike");
            var foe = Spawn("CFRunesmithFoe", 1);
            SEntMan.GetComponent<CFModeChoiceComponent>(action).Chosen = 1;
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = foe });
            known = RunesOn(foe);

            var other = Spawn("CFRunesmithFoe", 1);
            SEntMan.GetComponent<CFModeChoiceComponent>(action).Chosen = 3;
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = other });
            unknown = RunesOn(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(known, Is.EqualTo(1), "Atryl is known.");
            Assert.That(unknown, Is.EqualTo(0), "Camonica is not.");
        });
    }

    /// <summary>Remote Detonation invokes the runes on the target it hits.</summary>
    [Test]
    public async Task RemoteDetonationInvokesOnTheTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, after = 0, hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Atryl");
            GiveFeat(hero, "CFRemoteDetonation");
            var hands = SEntMan.System<SharedHandsSystem>();
            hands.TryPickupAnyHand(hero, Spawn("CFShortbow"), animate: false);
            var foe = Spawn("CFRunesmithFoe", 3);
            Actions.PerformAction(hero, Action(hero, "CFActTraceAtryl"), new CFActivityTargetEvent { Target = foe });
            before = RunesOn(foe);
            var hp = SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
            Actions.PerformAction(hero, Action(hero, "CFActRemoteDetonation"), new CFActivityTargetEvent { Target = foe });
            after = RunesOn(foe);
            hurt = hp - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(after, Is.EqualTo(0), "The rune was invoked.");
            Assert.That(hurt, Is.GreaterThan(0));
        });
    }

    /// <summary>Terrifying Invocation Demoralizes and invokes in one action.</summary>
    [Test]
    public async Task TerrifyingInvocationFrightensAndInvokes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var runes = 1;
        var frightened = 0;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Ledria");
            SEntMan.GetComponent<CFCombatantComponent>(hero).TrainedSkills.Add("Intimidation");
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Charisma] = 40;
            GiveFeat(hero, "CFTerrifyingInvocation");
            var foe = Spawn("CFRunesmithFoe", 1);
            Actions.PerformAction(hero, Action(hero, "CFActTraceLedria"), new CFActivityTargetEvent { Target = foe });
            Actions.PerformAction(hero, Action(hero, "CFActTerrifyingInvocation"), new CFActivityTargetEvent { Target = foe });
            runes = RunesOn(foe);
            frightened = SEntMan.GetComponent<CFConditionComponent>(foe).Frightened;
        });

        Assert.Multiple(() =>
        {
            Assert.That(runes, Is.EqualTo(0));
            Assert.That(frightened, Is.GreaterThan(0));
        });
    }

    /// <summary>Transpose Etching moves the newest rune to another bearer.</summary>
    [Test]
    public async Task TransposeEtchingMovesARune()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 1, second = 0;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Lyskel");
            GiveFeat(hero, "CFTransposeEtching");
            var foe = Spawn("CFRunesmithFoe", 1);
            var other = Spawn("CFRunesmithFoe", 2);
            Actions.PerformAction(hero, Action(hero, "CFActTraceLyskel"), new CFActivityTargetEvent { Target = foe });
            Actions.PerformAction(hero, Action(hero, "CFActTransposeEtching"), new CFActivityTargetEvent { Target = other });
            first = RunesOn(foe);
            second = RunesOn(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(0));
            Assert.That(second, Is.EqualTo(1));
        });
    }

    /// <summary>A Rune-Singer traces from afar for 1 action once per 10 minutes. Smithing Weapons sparks on a rune-bearer.</summary>
    [Test]
    public async Task RuneSingerAndSmithingWeapons()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int sung = 0, paid = 0, sparks = 0;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Atryl");
            GiveFeat(hero, "CFRuneSinger");
            GiveFeat(hero, "CFSmithingWeapons");
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            var foe = Spawn("CFRunesmithFoe", 4);
            var start = pool.Stamina;
            Actions.PerformAction(hero, Action(hero, "CFActTraceAtryl"), new CFActivityTargetEvent { Target = foe });
            sung = (int) (start - pool.Stamina);
            start = pool.Stamina;
            Actions.PerformAction(hero, Action(hero, "CFActTraceAtryl"), new CFActivityTargetEvent { Target = foe });
            paid = (int) (start - pool.Stamina);

            var hands = SEntMan.System<SharedHandsSystem>();
            hands.TryPickupAnyHand(hero, Spawn("CFDagger"), animate: false);
            var near = Spawn("CFRunesmithFoe", 1);
            Actions.PerformAction(hero, Action(hero, "CFActTraceAtryl"), new CFActivityTargetEvent { Target = near });
            var strikes = SEntMan.System<CFStrikeSystem>();
            strikes.TryMeleeWeapon(hero, out var stats, out var weapon);
            var hp = SEntMan.GetComponent<CFCombatantComponent>(near).CurrentHp;
            var damage = 0;
            strikes.PerformStrike(hero, near, stats, weapon, melee: true, resolved: (_, dealt) => damage = dealt);
            sparks = hp - SEntMan.GetComponent<CFCombatantComponent>(near).CurrentHp - damage;
        });

        Assert.Multiple(() =>
        {
            Assert.That(sung, Is.EqualTo(1), "The song pays the second action.");
            Assert.That(paid, Is.EqualTo(2), "Once per 10 minutes.");
            Assert.That(sparks, Is.EqualTo(1), "1 fire more than the Strike itself.");
        });
    }

    /// <summary>A Runic Tattoo is etched outside the limit, fades when invoked, and comes back with a long rest.</summary>
    [Test]
    public async Task RunicTattooFadesAndReturns()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, afterInvoke = 1, restored = 0;
        var faded = false;
        var counted = true;
        await Server.WaitPost(() =>
        {
            var hero = Runesmith("Rune1Sertum");
            SEntMan.GetComponent<CFExperienceComponent>(hero).OptionPicks.Add(new CFOptionPick { Source = "CFRunicTattoo", Slot = 0, Value = "pick:TattooSertum" });
            GiveFeat(hero, "CFRunicTattoo");
            before = RunesOn(hero);
            counted = SEntMan.GetComponent<CFRunesmithComponent>(hero).Etched.Contains(hero);
            Actions.PerformAction(hero, Action(hero, "CFActInvokeRune"), new CFActivityTargetEvent { Target = hero });
            afterInvoke = RunesOn(hero);
            faded = SEntMan.GetComponent<CFRunesmithComponent>(hero).TattooFaded;
            Runes.RestoreTattoo(hero);
            restored = RunesOn(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(counted, Is.False, "It is outside the etched limit.");
            Assert.That(afterInvoke, Is.EqualTo(0));
            Assert.That(faded, Is.True);
            Assert.That(restored, Is.EqualTo(1));
        });
    }

    /// <summary>Zohk calls its bearer to the Runesmith's side, and Pluuna dazzles the enemies around its bearer.</summary>
    [Test]
    public async Task ZohkCallsAndPluunaDazzles()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var distance = 10f;
        var dazzled = 0;
        await Server.WaitPost(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var hero = Runesmith("Rune1Zohk", "Rune2Pluuna");
            var friend = Spawn(x: 5);
            Actions.PerformAction(hero, Action(hero, "CFActTraceZohk"), new CFActivityTargetEvent { Target = friend });
            Actions.PerformAction(hero, Action(hero, "CFActInvokeRune"), new CFActivityTargetEvent { Target = friend });
            distance = (transform.GetWorldPosition(friend) - transform.GetWorldPosition(hero)).Length();

            var foe = Spawn("CFRunesmithFoe", 2);
            var bystander = Spawn("CFRunesmithFoe", 3);
            Actions.PerformAction(hero, Action(hero, "CFActTracePluuna"), new CFActivityTargetEvent { Target = foe });
            Actions.PerformAction(hero, Action(hero, "CFActInvokeRune"), new CFActivityTargetEvent { Target = foe });
            dazzled = SEntMan.System<CFConditionSystem>().Value(bystander, CFConditionKind.Dazzled);
        });

        Assert.Multiple(() =>
        {
            Assert.That(distance, Is.LessThan(1.5f));
            Assert.That(dazzled, Is.EqualTo(1), "Wisdom -40 cannot pass the save.");
        });
    }

    /// <summary>Every Runesmith feat applies.</summary>
    [Test]
    public async Task EveryRunesmithFeatCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class == "Runesmith"))
            {
                Features.Apply(Spawn(), feat.Grants, feat.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(19));
    }
}
