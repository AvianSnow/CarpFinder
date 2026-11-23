using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Champion's cause feats, Blessing of the Devoted, Mercy and Cruelty.</summary>
[TestFixture]
[TestOf(typeof(CFGrantChangeReaction))]
public sealed class CFChampionFeatTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFClassChoicePrototype> Grandeur = "CauseGrandeur";
    private static readonly ProtoId<CFClassChoicePrototype> Redemption = "CauseRedemption";
    private static readonly ProtoId<CFClassChoicePrototype> Iniquity = "CauseIniquity";
    private static readonly ProtoId<CFFeatPrototype> BrilliantFlash = "CFBrilliantFlash";
    private static readonly ProtoId<CFFeatPrototype> WeightOfGuilt = "CFWeightofGuilt";
    private static readonly ProtoId<CFFeatPrototype> ViciousVengeance = "CFViciousVengeance";
    private static readonly ProtoId<CFFeatPrototype> Mercy = "CFMercy";
    private static readonly ProtoId<CFClassPrototype> Champion = "Champion";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFChampionFeatTestHero
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
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id = "CFChampionFeatTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid WithCause(ProtoId<CFClassChoicePrototype> cause)
    {
        var hero = Spawn();
        Features.Apply(hero, Server.ProtoMan.Index(cause).Grants, $"choice:{cause.Id}");
        return hero;
    }

    private List<CFEffect> OnEnemy(EntityUid hero)
    {
        return SEntMan.GetComponent<CFReactionsComponent>(hero).Known[0].Reaction.OnEnemy;
    }

    [Test]
    public async Task ACauseFeatChangesOnlyItsOwnersReaction()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = -1, flash = -1, prototype = -1, vengeance = -1, vengeanceBefore = -1;
        bool stupefies = false;
        await Server.WaitPost(() =>
        {
            var other = WithCause(Grandeur);
            var hero = WithCause(Grandeur);
            Features.ApplyFeat(hero, Server.ProtoMan.Index(BrilliantFlash));
            plain = OnEnemy(other).Count;
            flash = OnEnemy(hero).Count;
            prototype = Server.ProtoMan.Index(Grandeur).Grants.OfType<CFGrantReaction>().Single().Reaction.OnEnemy.Count;

            var guilty = WithCause(Redemption);
            Features.ApplyFeat(guilty, Server.ProtoMan.Index(WeightOfGuilt));
            stupefies = OnEnemy(guilty).Count == 1
                        && OnEnemy(guilty)[0] is CFApplyConditionEffect { Condition: CFConditionKind.Stupefied, Value: 2 };

            var vicious = WithCause(Iniquity);
            vengeanceBefore = OnEnemy(vicious).Count;
            Features.ApplyFeat(vicious, Server.ProtoMan.Index(ViciousVengeance));
            vengeance = OnEnemy(vicious).Count;
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(0), "Another Champion's Flash of Grandeur is untouched");
            Assert.That(flash, Is.EqualTo(1), "Brilliant Flash adds off-guard");
            Assert.That(prototype, Is.EqualTo(0), "And so is the prototype's");
            Assert.That(stupefies, Is.True, "Weight of Guilt replaces enfeebled with stupefied");
            Assert.That(vengeance, Is.EqualTo(vengeanceBefore + 1));
        });
    }

    [Test]
    public async Task TheBlessingIsAChoiceAndABlessedShieldBlocksMore()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool blessed = false;
        int through = 0, plainThrough = 0;
        await Server.WaitPost(() =>
        {
            var feature = Server.ProtoMan.Index(Champion).Features.Single(f => f.Name == "Blessing of the Devoted");
            var hero = Spawn();
            var xp = SEntMan.EnsureComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "class:Champion:Blessing of the Devoted", Slot = 0, Value = "pick:BlessedShield" });
            Features.Apply(hero, feature.Grants, $"class:Champion:{feature.Level}:{feature.Name}");
            blessed = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).BlessedShield;

            var shields = SEntMan.System<CFShieldSystem>();
            var shield = Spawn("CFSteelShield");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, shield);
            through = shields.Block(hero, (shield, SEntMan.GetComponent<CFShieldComponent>(shield)), 20);

            var other = Spawn(x: 1);
            var plainShield = Spawn("CFSteelShield", 1);
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(other, plainShield);
            plainThrough = shields.Block(other, (plainShield, SEntMan.GetComponent<CFShieldComponent>(plainShield)), 20);
        });

        Assert.Multiple(() =>
        {
            Assert.That(blessed, Is.True, "The pick of a class feature is found by its grants");
            Assert.That(plainThrough - through, Is.EqualTo(3), "3 more Hardness");
        });
    }

    [Test]
    public async Task MercyEndsAConditionItCoversAndCrueltyOpensTheTwoActionTouch()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int frightened = -1, clumsy = -1;
        bool before = true, after = false;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Spawn();
            var xp = SEntMan.EnsureComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFMercy", Slot = 0, Value = "pick:MercyMind" });
            Features.ApplyFeat(hero, Server.ProtoMan.Index(Mercy));

            var ally = Spawn(x: 1);
            conditions.ApplyFrightened(ally, 2);
            conditions.ApplyCondition(ally, CFConditionKind.Clumsy, 1, 60f, hero, 0);
            SEntMan.System<CFEffectSystem>().Run(new List<CFEffect> { new CFMercyEffect() }, new CFEffectArgs
            {
                User = hero,
                Source = hero,
                Target = ally,
                Position = SEntMan.GetComponent<TransformComponent>(ally).Coordinates,
            });
            frightened = conditions.Frightened(ally);
            clumsy = conditions.Clumsy(ally);

            var mode = new CFModeOption { Requires = CFModeRequirement.Cruelty };
            before = CFModeRequirements.Met(SEntMan, hero, mode);
            Features.SetOption(hero, CFCombatOption.Cruelty);
            after = CFModeRequirements.Met(SEntMan, hero, mode);
        });

        Assert.Multiple(() =>
        {
            Assert.That(frightened, Is.EqualTo(0), "Mercy of the Mind ends frightened");
            Assert.That(clumsy, Is.EqualTo(1), "And leaves what it does not cover");
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
        });
    }
}
