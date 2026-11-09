using System.Collections.Generic;
using System.Numerics;
using Content.Client.CF.Hud;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>What the HUD and sheet read matches what a roll uses, and reaches a real client.</summary>
[TestFixture]
[TestOf(typeof(CFConditionSystem))]
public sealed class CFBonusDisplayTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFDisplayHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 15
    classId: Fighter
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 2
      Intelligence: 0
      Wisdom: 1
      Charisma: 0
  - type: CFCombatOptions
";

    private CFDevMap _map = default!;

    /// <summary>The bonus crosses the network, so this also proves it can be sent.</summary>
    [Test]
    public async Task ABonusReachesTheClientAndLeavesItAgain()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFDisplayHero", _map.Local(0, 0));
            var bonuses = SEntMan.System<CFBonusSystem>();
            bonuses.AddBonus(hero, CFBonusStat.ArmorClass, CFBonusType.Circumstance, 2, 1f);
            bonuses.AddPermanentBonus(hero, CFBonusStat.Will, CFBonusType.Status, 1, "Fear", null);
        });
        await Pair.RunTicksSync(5);

        var bonuses = CEntMan.System<CFBonusSystem>();
        var clientHero = CEntMan.GetEntity(SEntMan.GetNetEntity(hero));
        await Client.WaitAssertion(() =>
        {
            Assert.That(bonuses.Total(clientHero, CFBonusStat.ArmorClass), Is.EqualTo(2));
            Assert.That(bonuses.TimedTotal(clientHero, CFBonusStat.ArmorClass), Is.EqualTo(2));
            Assert.That(CEntMan.System<CFConditionSystem>().ArmorClassModifier(clientHero), Is.EqualTo(2));
        });

        await Pair.RunTicksSync(60);
        await Client.WaitAssertion(() =>
        {
            Assert.That(bonuses.Total(clientHero, CFBonusStat.ArmorClass), Is.Zero, "It ran out on the client too.");
        });
    }

    [Test]
    public async Task TheShownModifiersAreTheOnesARollUses()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFDisplayHero", _map.Local(0, 0));
            var conditions = SEntMan.System<CFConditionSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);

            Assert.That(conditions.DefenseModifier(hero, CFDefenseType.Fortitude), Is.EqualTo(2 + 1 + 4), "Expert");
            Assert.That(conditions.ArmorClassModifier(hero), Is.Zero);

            bonuses.AddBonus(hero, CFBonusStat.Saves, CFBonusType.Status, 1, 60f);
            bonuses.AddBonus(hero, CFBonusStat.ArmorClass, CFBonusType.Status, 1, 60f);
            conditions.ApplyCondition(hero, CFConditionKind.Frightened, 2, 60f, hero, 0);
            options.Raging = true;

            Assert.Multiple(() =>
            {
                Assert.That(conditions.DefenseModifier(hero, CFDefenseType.Fortitude), Is.EqualTo(2 + 1 + 4 + 1 - 2));
                Assert.That(conditions.ArmorClassModifier(hero), Is.EqualTo(1 - 2 - 1));
                Assert.That(bonuses.TimedTotal(hero, CFBonusStat.Saves), Is.EqualTo(1));
            });

            bonuses.AddPermanentBonus(hero, CFBonusStat.Perception, CFBonusType.Status, 2);
            Assert.That(bonuses.TimedTotal(hero, CFBonusStat.Perception), Is.Zero, "A feat is not news on the HUD.");
        });
    }

    /// <summary>The HUD is only built in a round, so this is what proves its XAML loads.</summary>
    [Test]
    public async Task TheBarIsLaidOutAsDrawn()
    {
        await Client.WaitAssertion(() =>
        {
            var book = new CFSpellbookComponent();
            book.Slots.AddRange(new[] { 5, 3, 2, 2, 2, 2 });
            book.Expended.AddRange(new[] { 0, 1, 0, 0, 0, 0 });

            var hud = new CFGameHud();
            hud.SetCombatant(true);
            hud.Deck.SetBook(book);
            hud.Defenses.Visible = true;
            hud.Measure(new Vector2(1600f, 600f));
            hud.Arrange(UIBox2.FromDimensions(0f, 0f, 1600f, 600f));
            var closed = hud.DesiredSize.Y;

            Assert.Multiple(() =>
            {
                Assert.That(hud.Armor.GlobalPosition.X, Is.LessThan(hud.HpBar.GlobalPosition.X), "AC is left of the bars.");
                Assert.That(hud.Armor.GlobalPosition.Y, Is.LessThan(hud.Defenses.GlobalPosition.Y), "AC is above the saves.");
                Assert.That(hud.HpBar.GlobalPosition.X, Is.LessThan(hud.TempBar.GlobalPosition.X));
                Assert.That(hud.HpBar.GlobalPosition.Y, Is.LessThan(hud.Hands.GlobalPosition.Y), "The bars lie across the item row.");
            });

            // A control only measures again on a real frame, so each case gets a HUD of its own.
            var withConditions = new CFGameHud();
            withConditions.SetCombatant(true);
            withConditions.Defenses.Visible = true;
            withConditions.Status.Set(new List<CFHudStatus> { new("Prone", false), new("Attack +1", true) });
            withConditions.Measure(new Vector2(1600f, 600f));
            Assert.That(withConditions.DesiredSize.Y, Is.EqualTo(closed), "Open bags move if the HUD changes height.");

            var open = new CFGameHud();
            open.SetCombatant(true);
            open.Defenses.Visible = true;
            open.DeckClip.Openness = 1f;
            open.Measure(new Vector2(1600f, 600f));
            Assert.That(open.DesiredSize.Y, Is.GreaterThan(closed + 64f), "The deck adds to the bar's height when it opens.");
        });
    }

    [Test]
    public async Task SickenedIsNamedAndEnds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitAssertion(() =>
        {
            hero = SEntMan.SpawnEntity("CFDisplayHero", _map.Local(0, 0));
            SEntMan.System<CFConditionSystem>().ApplyCondition(hero, CFConditionKind.Sickened, 2, 1f, hero, 0);
            Assert.That(SEntMan.GetComponent<CFConditionComponent>(hero).SickenedValue, Is.EqualTo(2));
        });

        await Server.WaitRunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CFConditionComponent>(hero).SickenedValue, Is.Zero);
        });
    }
}
