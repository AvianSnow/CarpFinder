using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>
/// The remaster Feint and the options built on it: the Scoundrel racket, Distracting and Overextending Feint, Outwit's
/// skill bonus, Hunter's Aim ignoring concealment, and advanced weapons stopping one rank short of a mastered group.
/// </summary>
[TestFixture]
[TestOf(typeof(CFConditionSystem))]
public sealed class CFFeintTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFeintTestHero
  components:
  - type: CFCombatant
    level: 3
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFFeintTestHero
  id: CFFeintTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFLongsword
  id: CFFeintTestAdvancedSword
  components:
  - type: CFWeapon
    damageDie: 8
    damageType: Slashing
    category: Advanced
    group: Sword
    hands: One
";

    private CFDevMap _map = default!;

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Spawn(int x, string id = "CFFeintTestHero")
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void Option(EntityUid mob, CFCombatOption option)
    {
        SEntMan.System<CFCharacterFeatureSystem>().SetOption(mob, option);
    }

    [Test]
    public async Task ASuccessIsGoodForOneMeleeAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool melee = false, ranged = true, other = true, afterAttack = true, criticalAfterAttack = false;
        await Server.WaitPost(() =>
        {
            var rogue = Spawn(0);
            var friend = Spawn(0);
            var foe = Spawn(1, "CFFeintTestFoe");
            Conditions.ApplyFeint(rogue, foe, critical: false);
            melee = Conditions.IsOffGuardTo(foe, rogue);
            ranged = Conditions.IsOffGuardTo(foe, rogue, melee: false);
            other = Conditions.IsOffGuardTo(foe, friend);
            Conditions.NoteAttackRoll(rogue, foe, melee: true);
            afterAttack = Conditions.IsOffGuardTo(foe, rogue);

            var second = Spawn(1, "CFFeintTestFoe");
            Conditions.ApplyFeint(rogue, second, critical: true);
            Conditions.NoteAttackRoll(rogue, second, melee: true);
            criticalAfterAttack = Conditions.IsOffGuardTo(second, rogue);
        });

        Assert.Multiple(() =>
        {
            Assert.That(melee, Is.True);
            Assert.That(ranged, Is.False, "A Feint is against melee attacks only.");
            Assert.That(other, Is.False, "Only against the one who Feinted.");
            Assert.That(afterAttack, Is.False, "A success covers one attack.");
            Assert.That(criticalAfterAttack, Is.True, "A critical success lasts the 6 seconds.");
        });
    }

    [Test]
    public async Task AScoundrelFeintsHarder()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool afterAttack = false, friendOnCritical = false, perceptionDown = false;
        await Server.WaitPost(() =>
        {
            var rogue = Spawn(0);
            Option(rogue, CFCombatOption.ScoundrelFeint);
            Option(rogue, CFCombatOption.DistractingFeint);
            var friend = Spawn(0);
            var foe = Spawn(1, "CFFeintTestFoe");

            Conditions.ApplyFeint(rogue, foe, critical: false);
            Conditions.NoteAttackRoll(rogue, foe, melee: true);
            afterAttack = Conditions.IsOffGuardTo(foe, rogue);
            perceptionDown = SEntMan.System<CFBonusSystem>().Total(foe, CFBonusStat.Perception) == -2;

            var second = Spawn(1, "CFFeintTestFoe");
            Conditions.ApplyFeint(rogue, second, critical: true);
            friendOnCritical = Conditions.IsOffGuardTo(second, friend);
        });

        Assert.Multiple(() =>
        {
            Assert.That(afterAttack, Is.True, "A Scoundrel's success lasts 6 seconds.");
            Assert.That(friendOnCritical, Is.True, "A Scoundrel's critical success counts for every melee attacker.");
            Assert.That(perceptionDown, Is.True, "Distracting Feint lowers Perception.");
        });
    }

    [Test]
    public async Task OverextendingFeintCostsTheTargetOneAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, after = 0, againstOther = 0, prone = 0;
        await Server.WaitPost(() =>
        {
            var rogue = Spawn(0);
            var friend = Spawn(0);
            var foe = Spawn(1, "CFFeintTestFoe");
            Conditions.Overextend(foe, rogue, once: true);
            before = Conditions.CircumstanceAttackPenalty(foe, rogue);
            againstOther = Conditions.CircumstanceAttackPenalty(foe, friend);
            Conditions.NoteAttackRoll(foe, rogue, melee: true);
            after = Conditions.CircumstanceAttackPenalty(foe, rogue);

            Conditions.Overextend(foe, rogue, once: false);
            Conditions.KnockProne(foe);
            prone = Conditions.CircumstanceAttackPenalty(foe, rogue);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(-2));
            Assert.That(againstOther, Is.EqualTo(0));
            Assert.That(after, Is.EqualTo(0), "A success covers one attack roll.");
            Assert.That(prone, Is.EqualTo(-2), "Prone and overextended are both circumstance penalties, so they do not stack.");
        });
    }

    [Test]
    public async Task OutwitAndHuntersAim()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int outwit = 0, notPrey = 0;
        bool plain = true, aimed = false;
        await Server.WaitPost(() =>
        {
            var ranger = Spawn(0);
            var prey = Spawn(1, "CFFeintTestFoe");
            var other = Spawn(1, "CFFeintTestFoe");
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(ranger);
            options.PreyOutwit = true;
            options.Prey = prey;
            outwit = Conditions.OutwitBonus(ranger, prey);
            notPrey = Conditions.OutwitBonus(ranger, other);

            // Concealed with a DC no d20 can reach, so only ignoring it passes.
            var cond = Conditions.Ensure(prey);
            cond.Concealed = true;
            cond.FlatCheckDc = 21;
            plain = Conditions.PassFlatCheck(ranger, prey);
            aimed = Conditions.PassFlatCheck(ranger, prey, ignoreConcealed: true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(outwit, Is.EqualTo(2));
            Assert.That(notPrey, Is.EqualTo(0));
            Assert.That(plain, Is.False);
            Assert.That(aimed, Is.True);
        });
    }

    [Test]
    public async Task AMasteredGroupMakesAdvancedWeaponsExpert()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank martial = default, advanced = default;
        await Server.WaitPost(() =>
        {
            var fighter = Spawn(0);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(fighter);
            combat.ClassId = "Fighter";
            combat.ProficiencyRanks["Sword"] = CFProficiencyRank.Master;

            var weapons = SEntMan.System<CFWeaponSystem>();
            var longsword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            var exotic = SEntMan.SpawnEntity("CFFeintTestAdvancedSword", _map.Local(0, 0));
            weapons.TryStats(longsword, out var longStats);
            weapons.TryStats(exotic, out var exoticStats);
            martial = weapons.Rank(combat, longStats);
            advanced = weapons.Rank(combat, exoticStats);
        });

        Assert.Multiple(() =>
        {
            Assert.That(martial, Is.EqualTo(CFProficiencyRank.Master));
            Assert.That(advanced, Is.EqualTo(CFProficiencyRank.Expert));
        });
    }
}
