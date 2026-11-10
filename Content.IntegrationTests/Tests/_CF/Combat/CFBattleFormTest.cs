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
}
