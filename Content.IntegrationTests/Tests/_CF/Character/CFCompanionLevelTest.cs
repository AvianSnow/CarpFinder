using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Server.CF.Companions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Character;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>A companion is always the same level as the one it follows.</summary>
[TestFixture]
[TestOf(typeof(CFCompanionSystem))]
public sealed class CFCompanionLevelTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCompanionLevelHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private EntityUid SpawnHere(string id) => SEntMan.SpawnEntity(id, _map.Local(0, 0));

    private EntityUid Construct(EntityUid hero)
    {
        var construct = SpawnHere("CFCompanionConstruct");
        SEntMan.System<CFCompanionSystem>().Bind(hero, construct);
        SEntMan.GetComponent<CFCompanionComponent>(construct).PetId = "CFPetConstruct";
        return construct;
    }

    private void GainLevels(EntityUid hero, int levels)
    {
        SEntMan.System<CFExperienceSystem>().AddXpAndLevelUp(hero, levels * CFExperienceComponent.XpPerLevel);
    }

    [Test]
    public async Task ACompanionLevelsWithItsOwner()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SpawnHere("CFCompanionLevelHero");
            var construct = Construct(hero);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(construct);
            combat.MaxHp = 18;
            combat.CurrentHp = 10;

            GainLevels(hero, 2);

            Assert.That(combat.Level, Is.EqualTo(3));
            Assert.That(combat.ArmorClass, Is.EqualTo(18), "10 + level 3 + Dexterity 3 + trained 2.");
            Assert.That(combat.MaxHp, Is.EqualTo(34), "10 + (6 + Constitution 2) for 3 levels.");
            Assert.That(combat.CurrentHp, Is.EqualTo(26), "It keeps its wounds and gains the new Hit Points.");
        });
    }

    [Test]
    public async Task AFallenCompanionGainsNoHitPointsYet()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SpawnHere("CFCompanionLevelHero");
            var construct = Construct(hero);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(construct);
            combat.CurrentHp = 0;

            GainLevels(hero, 1);

            Assert.That(combat.Level, Is.EqualTo(2));
            Assert.That(combat.CurrentHp, Is.EqualTo(0));
            Assert.That(combat.MaxHp, Is.EqualTo(26));
        });
    }

    [Test]
    public async Task OnlyTheOwnersCompanionLevels()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SpawnHere("CFCompanionLevelHero");
            var stranger = SpawnHere("CFCompanionLevelHero");
            var theirs = Construct(stranger);

            GainLevels(hero, 1);

            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(theirs).Level, Is.EqualTo(1));
        });
    }

    /// <summary>A summon that did not come from a <c>cfPet</c> has no table to level by.</summary>
    [Test]
    public async Task ASummonWithNoPetIsLeftAlone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SpawnHere("CFCompanionLevelHero");
            var summon = SpawnHere("CFCompanionConstruct");
            SEntMan.System<CFCompanionSystem>().Bind(hero, summon);

            GainLevels(hero, 1);

            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(summon).Level, Is.EqualTo(1));
        });
    }
}
