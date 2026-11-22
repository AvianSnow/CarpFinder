using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>The Bard's compositions besides Courageous Anthem, the muses' feats, and Speed bonuses reaching movement.</summary>
[TestFixture]
[TestOf(typeof(CFCompositionSystem))]
public sealed class CFCompositionTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCompositionTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Spawn(int x)
    {
        return SEntMan.SpawnEntity("CFCompositionTestHero", _map.Local(x, 0));
    }

    private void Perform(EntityUid bard, string action)
    {
        var uid = SEntMan.SpawnEntity(action, MapCoordinates.Nullspace);
        SEntMan.EventBus.RaiseLocalEvent(uid, new CFActivityInstantEvent { Performer = bard });
    }

    [Test]
    public async Task OneCompositionAtATime()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid bard = default, ally = default;
        await Server.WaitPost(() =>
        {
            bard = Spawn(0);
            ally = Spawn(2);
            Perform(bard, "CFActRallyingAnthem");
        });

        await Pair.RunSeconds(0.5f);
        int rallyingAc = 0, afterAc = 0, speed = 0;
        float sprint = 0;
        await Server.WaitPost(() =>
        {
            rallyingAc = Bonuses.Total(ally, CFBonusStat.ArmorClass);
            Perform(bard, "CFActTripleTime");
        });

        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            afterAc = Bonuses.Total(ally, CFBonusStat.ArmorClass);
            speed = Bonuses.Total(ally, CFBonusStat.Speed);
            sprint = SEntMan.GetComponent<MovementSpeedModifierComponent>(ally).SprintSpeedModifier;
        });

        Assert.Multiple(() =>
        {
            Assert.That(rallyingAc, Is.EqualTo(1));
            Assert.That(afterAc, Is.EqualTo(0), "Triple Time ended Rallying Anthem.");
            Assert.That(speed, Is.EqualTo(10));
            Assert.That(sprint, Is.GreaterThan(1.01f), "A Speed bonus makes the creature faster.");
        });
    }

    [Test]
    public async Task MusesGiveTheirFeats()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool warrior = false, enigma = false, polymath = false;
        await Server.WaitPost(() =>
        {
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            bool Gives(string muse, string feat)
            {
                var bard = Spawn(0);
                features.ApplyClassChoice(bard, Server.ProtoMan.Index(new ProtoId<CFClassChoicePrototype>(muse)), 1);
                return features.HasFeat(bard, feat);
            }

            warrior = Gives("MuseWarrior", "CFMartialPerformance");
            enigma = Gives("MuseEnigma", "CFBardicLore");
            polymath = Gives("MusePolymath", "CFVersatilePerformance");
        });

        Assert.Multiple(() =>
        {
            Assert.That(warrior, Is.True);
            Assert.That(enigma, Is.True);
            Assert.That(polymath, Is.True);
        });
    }

    [Test]
    public async Task VersatilePerformanceDemoralizesWithPerformance()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, versatile = 0;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var bard = Spawn(0);
            var foe = Spawn(1);
            SEntMan.GetComponent<CFCombatantComponent>(bard).TrainedSkills.Add("Performance");
            conditions.TryGetDemoralizeCheck(bard, foe, out plain, out _);
            SEntMan.GetComponent<CFCombatOptionsComponent>(bard).VersatilePerformance = true;
            conditions.TryGetDemoralizeCheck(bard, foe, out versatile, out _);
        });

        Assert.That(versatile, Is.GreaterThan(plain), "Trained Performance beats untrained Intimidation.");
    }

    [Test]
    public async Task HymnOfHealingIsShortAndLoremastersEtudeIsAReaction()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        float sustain = 0;
        bool etude = false;
        await Server.WaitPost(() =>
        {
            var hymn = SEntMan.SpawnEntity("CFHymnOfHealing", MapCoordinates.Nullspace);
            sustain = SEntMan.GetComponent<CFSpellComponent>(hymn).SustainSeconds;

            var bard = Spawn(0);
            var feat = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFLoremastersEtude"));
            SEntMan.System<CFCharacterFeatureSystem>().ApplyFeat(bard, feat);
            etude = SEntMan.GetComponent<CFReactionsComponent>(bard).Known.Any(k => k.Reaction.FocusPoints == 1 && k.Reaction.Rerolls);
        });

        Assert.Multiple(() =>
        {
            Assert.That(sustain, Is.EqualTo(24f));
            Assert.That(etude, Is.True);
        });
    }
}
