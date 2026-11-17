using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Weapons.Melee;
using Content.Shared.CF.Character;
using Content.Shared.CF.Actions;
using Content.Shared.Actions;
using Content.Shared.ActionBlocker;
using System;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Animal Form: picking an animal, what the form changes, and putting it all back.</summary>
[TestFixture]
[TestOf(typeof(CFBattleFormSystem))]
public sealed class CFBattleFormTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFBattleFormTestHero
  components:
  - type: CFCombatant
    level: 3
    armorClass: 12
    maxHp: 30
    currentHp: 30
  - type: CFAbilityScore
  - type: CFCombatOptions
";

    private CFDevMap _map = default!;

    [Test]
    public async Task ABearFightsWithItsJawsAndGoesBackAfterward()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var picked = false;
        int ac = 0, die = 0, temp = 0;
        var castsInForm = true;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFBattleFormTestHero", _map.Local(0, 0));
            var action = SEntMan.SpawnEntity("CFAnimalForm", MapCoordinates.Nullspace);
            var pick = new CFModeImmediateCastEvent(hero, 2);
            SEntMan.EventBus.RaiseLocalEvent(action, ref pick);
            picked = pick.Success;

            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            ac = combat.ArmorClass;
            temp = combat.TemporaryHp;
            die = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).NaturalDie;
            castsInForm = SEntMan.System<CFSpellSystem>().HasSlot(hero, SEntMan.GetComponent<CFSpellComponent>(action));

            SEntMan.GetComponent<CFBattleFormComponent>(hero).Until = SGameTiming.CurTime;
        });

        await RunTicksSync(3);

        int acAfter = 0, dieAfter = -1;
        var still = true;
        await Server.WaitPost(() =>
        {
            acAfter = SEntMan.GetComponent<CFCombatantComponent>(hero).ArmorClass;
            dieAfter = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).NaturalDie;
            still = SEntMan.HasComponent<CFBattleFormComponent>(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(picked, Is.True);
            Assert.That(ac, Is.EqualTo(16 + 3), "The form's AC, because it is higher than 12.");
            Assert.That(die, Is.EqualTo(8), "A bear's jaws are 2d8.");
            Assert.That(temp, Is.EqualTo(5));
            Assert.That(castsInForm, Is.False, "No spells in a battle form.");
            Assert.That(still, Is.False, "It runs out.");
            Assert.That(acAfter, Is.EqualTo(12));
            Assert.That(dieAfter, Is.Not.EqualTo(8), "The fist is back.");
        });
    }

    private EntityUid Druid(int level)
    {
        var hero = SEntMan.SpawnEntity("CFBattleFormTestHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFCombatantComponent>(hero).Level = level;
        var order = Server.ProtoMan.Index(new ProtoId<CFClassChoicePrototype>("OrderUntamed"));
        SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, order.Grants, "choice:OrderUntamed");
        return hero;
    }

    private EntityUid? Action(EntityUid hero, string id)
    {
        foreach (var action in SEntMan.System<SharedActionsSystem>().GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        return null;
    }

    private bool Pick(EntityUid hero, byte mode)
    {
        var pick = new CFModeImmediateCastEvent(hero, mode);
        SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFUntamedForm")!.Value, ref pick);
        return pick.Success;
    }

    private const byte Bear = 2;
    private const byte Pest = 7;

    [Test]
    public async Task TheUntamedOrderBecomesAPestAtFirstAndCanDismissIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool bear = true, pest = false, canAttack = true, hasDismiss = false, formAfter = true, dismissAfter = true;
        int focusAfterBear = -1, focusAfterPest = -1, ac = 0, weak = 0, weakAfter = 0;
        var lasts = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            var hero = Druid(1);
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            var bonuses = SEntMan.System<CFBonusSystem>();
            bear = Pick(hero, Bear);
            focusAfterBear = book.FocusSpent;

            pest = Pick(hero, Pest);
            focusAfterPest = book.FocusSpent;
            ac = SEntMan.GetComponent<CFCombatantComponent>(hero).ArmorClass;
            lasts = SEntMan.GetComponent<CFBattleFormComponent>(hero).Until - SGameTiming.CurTime;
            weak = bonuses.ModifyDamage(hero, "Slashing", 10);
            var target = SEntMan.SpawnEntity("CFBattleFormTestHero", _map.Local(1, 0));
            canAttack = SEntMan.System<ActionBlockerSystem>().CanAttack(hero, target, (hero, SEntMan.GetComponent<MeleeWeaponComponent>(hero)));

            var dismiss = Action(hero, "CFActDismissForm");
            hasDismiss = dismiss != null;
            SEntMan.System<SharedActionsSystem>().PerformAction(hero, (dismiss!.Value, SEntMan.GetComponent<ActionComponent>(dismiss.Value)), new CFActivityInstantEvent());
            formAfter = SEntMan.HasComponent<CFBattleFormComponent>(hero);
            weakAfter = bonuses.ModifyDamage(hero, "Slashing", 10);
            dismissAfter = Action(hero, "CFActDismissForm") != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(bear, Is.False, "Animal shapes need rank 2, which is level 3.");
            Assert.That(focusAfterBear, Is.Zero, "A refused shape costs nothing.");
            Assert.That(pest, Is.True);
            Assert.That(focusAfterPest, Is.EqualTo(1));
            Assert.That(ac, Is.EqualTo(15 + 1));
            Assert.That(lasts, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(weak, Is.EqualTo(15), "Weakness 5 to physical damage.");
            Assert.That(canAttack, Is.False, "A pest cannot Strike.");
            Assert.That(hasDismiss, Is.True);
            Assert.That(formAfter, Is.False);
            Assert.That(weakAfter, Is.EqualTo(10), "The weakness goes with the form.");
            Assert.That(dismissAfter, Is.False, "The Dismiss action goes with the form.");
        });
    }

    [TestCase(3, 16, 5, 1)]
    [TestCase(5, 17, 10, 5)]
    public async Task UntamedFormUsesAnimalFormAtItsRank(int level, int baseAc, int tempHp, int damage)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var picked = false;
        int ac = 0, temp = 0, bonus = 0, own = 0;
        await Server.WaitPost(() =>
        {
            var hero = Druid(level);
            picked = Pick(hero, Bear);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            ac = combat.ArmorClass;
            temp = combat.TemporaryHp;
            bonus = SEntMan.System<CFBonusSystem>().Total(hero, CFBonusStat.MeleeDamage);
            own = SEntMan.GetComponent<CFBattleFormComponent>(hero).OwnAttackBonus;
        });

        Assert.Multiple(() =>
        {
            Assert.That(picked, Is.True);
            Assert.That(ac, Is.EqualTo(baseAc + level));
            Assert.That(temp, Is.EqualTo(tempHp));
            Assert.That(bonus, Is.EqualTo(damage));
            Assert.That(own, Is.EqualTo(2), "Untamed Form's bonus to the druid's own attack modifier.");
        });
    }

    [Test]
    public async Task FormControlMakesItWeakerAndAnHourLong()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool bear = true, pest = false, shapeLeft = true;
        var lasts = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            var hero = Druid(5);
            var shape = SEntMan.EnsureComponent<CFActiveSpellshapeComponent>(hero);
            shape.Shape = "CFActFormControl";
            shape.Expires = SGameTiming.CurTime + TimeSpan.FromSeconds(30);

            bear = Pick(hero, Bear);
            pest = Pick(hero, Pest);
            lasts = SEntMan.GetComponent<CFBattleFormComponent>(hero).Until - SGameTiming.CurTime;
            shapeLeft = SEntMan.HasComponent<CFActiveSpellshapeComponent>(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(bear, Is.False, "Rank 3 less 2 is rank 1, which only has the pest.");
            Assert.That(pest, Is.True);
            Assert.That(lasts, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(shapeLeft, Is.False, "The spellshape is used up.");
        });
    }
}
