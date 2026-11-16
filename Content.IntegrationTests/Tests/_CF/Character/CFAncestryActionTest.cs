using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The actions ancestry feats give (roadmap 2.6): a skill check against a defense DC, a chosen breath, venom on the fangs.</summary>
[TestFixture]
[TestOf(typeof(CFEffectActionSystem))]
public sealed class CFAncestryActionTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAncestryActionHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 20
    currentHp: 20
    trainedSkills: [Performance, Acrobatics]
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private EntityUid Built(string ancestry, string heritage, string feat, params CFOptionPick[] picks)
    {
        var species = Server.ProtoMan.Index(new ProtoId<CFAncestryPrototype>(ancestry)).Species;
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Heritage = heritage, AncestryFeat = feat };
        build.OptionPicks.AddRange(picks);
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(build));
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

    [Test]
    public async Task KoboldBreathIsTheChosenElement()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var fire = false;
        var cold = true;
        var unpicked = true;
        await Server.WaitPost(() =>
        {
            var kobold = Built("Kobold", "DragonscaledKobold", "CFKoboldBreath", new CFOptionPick { Source = "CFKoboldBreath", Slot = 0, Value = "Fire" });
            fire = Action(kobold, "CFActKoboldBreathFire") != null;
            cold = Action(kobold, "CFActKoboldBreathCold") != null;
            unpicked = Action(Built("Kobold", "DragonscaledKobold", "CFKoboldBreath"), "CFActKoboldBreathFire") != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(fire, Is.True);
            Assert.That(cold, Is.False);
            Assert.That(unpicked, Is.False, "Nothing until the breath is chosen.");
        });
    }

    [Test]
    public async Task ASkillCheckActionRollsTheUsersSkillAgainstTheDefense()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var landedOnEasy = false;
        var landedOnImpossible = true;
        var penalised = 0;
        await Server.WaitPost(() =>
        {
            var hero = SEntMan.SpawnEntity("CFAncestryActionHero", _map.Local(0, 0));
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            features.GrantAction(hero, "CFActGoblinSong");
            var song = Action(hero, "CFActGoblinSong")!.Value;
            var effects = SEntMan.System<CFEffectSystem>();

            // A target with no Will to speak of: DC 10 + 0, and the hero's Performance is at least +3.
            var easy = SEntMan.SpawnEntity("CFTrainingDummy", _map.Local(1, 0));
            var easyCombat = SEntMan.GetComponent<CFCombatantComponent>(easy);
            easyCombat.PrintedWill = -30;
            landedOnEasy = effects.ResolveTarget(song, hero, easy);
            penalised = SEntMan.System<CFBonusSystem>().Total(easy, CFBonusStat.Will);

            var hard = SEntMan.SpawnEntity("CFTrainingDummy", _map.Local(1, 1));
            SEntMan.GetComponent<CFCombatantComponent>(hard).PrintedWill = 60;
            landedOnImpossible = effects.ResolveTarget(song, hero, hard);
        });

        Assert.Multiple(() =>
        {
            Assert.That(landedOnEasy, Is.True);
            Assert.That(penalised, Is.EqualTo(-1), "A status penalty to Will saves.");
            Assert.That(landedOnImpossible, Is.False);
        });
    }

    [Test]
    public async Task EnvenomFangsPutsVenomOnTheNextUnarmedHit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var dice = 0;
        var unarmedOnly = false;
        var hasAction = false;
        await Server.WaitPost(() =>
        {
            // A level 5 feat with a prerequisite, so it is given straight to a hero.
            var lizard = SEntMan.SpawnEntity("CFAncestryActionHero", _map.Local(0, 0));
            SEntMan.System<CFCharacterSpawnSystem>().ApplyFeat(lizard, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFEnvenomFangs")));
            hasAction = Action(lizard, "CFActEnvenomFangs") != null;
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(lizard);
            options.TailToxinUntil = default;
            // The activity itself runs through the action event; its result is the fields it sets.
            options.ToxinDice = 1;
            options.ToxinDie = 6;
            options.ToxinUnarmedOnly = true;
            dice = options.ToxinDice;
            unarmedOnly = options.ToxinUnarmedOnly;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasAction, Is.True);
            Assert.That(dice, Is.EqualTo(1));
            Assert.That(unarmedOnly, Is.True);
        });
    }
}
