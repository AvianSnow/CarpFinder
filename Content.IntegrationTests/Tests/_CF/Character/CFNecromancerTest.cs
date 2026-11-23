using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Companions;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Necromancer's fatal methods, class features and feats, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFThrallServerSystem))]
public sealed class CFNecromancerTest : GameTest
{
    private static readonly Box2i Footprint = new(-2, -2, 7, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFNecroHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 400
    classId: Necromancer
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: 0
      Charisma: 0
  - type: CFCombatOptions
  - type: CFExperience
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFNecroHero
  id: CFNecroFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: 0
      Charisma: 0
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private EntityUid Spawn(string id = "CFNecroHero", int x = 0, int y = 0) => SEntMan.SpawnEntity(id, _map.Local(x, y));

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private void GiveFeature(EntityUid hero, string name)
    {
        var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Necromancer")).Features.First(f => f.Name == name);
        Features.Apply(hero, feature.Grants, name);
    }

    private EntityUid Necromancer(params string[] choices)
    {
        var hero = Spawn();
        GiveFeature(hero, "Grave Spells");
        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }

        return hero;
    }

    private void Raise(EntityUid hero, int count)
    {
        var ev = new CFCreateThrallsEvent(count);
        SEntMan.EventBus.RaiseLocalEvent(hero, ref ev);
    }

    private int CountThralls(EntityUid hero)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<CFThrallComponent>();
        while (query.MoveNext(out var uid, out var thrall))
        {
            if (thrall.Necromancer == hero && !SEntMan.Deleted(uid))
                count++;
        }

        return count;
    }

    private EntityUid FirstThrall(EntityUid hero)
    {
        var query = SEntMan.EntityQueryEnumerator<CFThrallComponent>();
        while (query.MoveNext(out var uid, out var thrall))
        {
            if (thrall.Necromancer == hero)
                return uid;
        }

        return default;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private int Hp(EntityUid mob) => SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    /// <summary>Consume Thrall: with no Focus Points left, a thrall within 30 feet is destroyed for one.</summary>
    [Test]
    public async Task ConsumeThrallTradesAThrallForAFocusPoint()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        int spent = -1, before = 0;
        await Server.WaitPost(() =>
        {
            hero = Necromancer("MethodPuppeteer");
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            book.FocusSpent = book.FocusPoints;
            Raise(hero, 1);
            before = CountThralls(hero);
            Actions.PerformAction(hero, Action(hero, "CFActConsumeThrall"), new CFActivityInstantEvent());
            spent = book.FocusSpent;
        });
        await Pair.RunTicksSync(3);

        Assert.Multiple(() =>
        {
            Assert.That(spent, Is.EqualTo(SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusPoints - 1));
            Assert.That(before, Is.EqualTo(2), "A Puppeteer raises one more.");
            Assert.That(CountThralls(hero), Is.EqualTo(1), "One is consumed.");
        });
    }

    /// <summary>Thrall Teamwork: a Reaper Strikes for free at an enemy beside a new thrall. Strength 40 always hits.</summary>
    [Test]
    public async Task ReaperStrikesWhenThrallsRise()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int reaper = 0, puppeteer = 0;
        await Server.WaitPost(() =>
        {
            var hero = Necromancer("MethodReaper");
            var foe = Spawn("CFNecroFoe", 1);
            Raise(hero, 2);
            reaper = 500 - Hp(foe);

            var other = Necromancer("MethodPuppeteer");
            SEntMan.System<SharedTransformSystem>().SetCoordinates(other, _map.Local(4, 0));
            var second = Spawn("CFNecroFoe", 5);
            Raise(other, 2);
            puppeteer = 500 - Hp(second);
        });

        Assert.Multiple(() =>
        {
            Assert.That(reaper, Is.GreaterThan(0));
            Assert.That(puppeteer, Is.EqualTo(0));
        });
    }

    /// <summary>Refocus with a thrall about destroys it and gives 2 Focus Points back.</summary>
    [Test]
    public async Task RefocusDestroysAThrallForASecondPoint()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var spent = -1;
        await Server.WaitPost(() =>
        {
            hero = Necromancer("FascinationBone");
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            book.FocusPoints = 2;
            book.FocusSpent = 2;
            Raise(hero, 1);
            SEntMan.System<CFFocusSystem>().Refocus((hero, book));
            spent = book.FocusSpent;
        });
        await Pair.RunTicksSync(3);

        Assert.Multiple(() =>
        {
            Assert.That(spent, Is.EqualTo(0));
            Assert.That(CountThralls(hero), Is.EqualTo(0));
        });
    }

    /// <summary>Inevitable Return: a thrall rises where an enemy died within 30 feet.</summary>
    [Test]
    public async Task InevitableReturnRaisesAThrallFromTheCorpse()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            GiveFeature(hero, "Inevitable Return");
            var foe = Spawn("CFNecroFoe", 3);
            SEntMan.System<CFDyingSystem>().Kill(foe, hero);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(CountThralls(hero), Is.EqualTo(1));
            var at = SEntMan.System<SharedTransformSystem>().GetWorldPosition(FirstThrall(hero));
            var corpse = _map.Local(3, 0).ToMap(SEntMan, SEntMan.System<SharedTransformSystem>()).Position;
            Assert.That((at - corpse).Length(), Is.LessThan(0.6f), "It rises on the corpse.");
        });
    }

    /// <summary>Body Shield: +2 AC when a thrall stands beside the Necromancer, and the thrall is spent.</summary>
    [Test]
    public async Task BodyShieldNeedsAThrallAndSpendsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree alone = CFDegree.CriticalFailure, shielded = CFDegree.CriticalFailure;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            var reactions = SEntMan.System<CFReactionSystem>();
            hero = Necromancer();
            GiveFeat(hero, "CFBodyShield");
            var foe = Spawn("CFNecroFoe", 1);

            // 10 + 5 against AC 14 hits. +2 makes it a miss.
            var ac = 14;
            alone = reactions.AnswerAttack(hero, foe, false, 10, 5, ref ac, CFDegree.Success);

            Raise(hero, 1);
            ac = 14;
            shielded = reactions.AnswerAttack(hero, foe, false, 10, 5, ref ac, CFDegree.Success);
        });
        await Pair.RunTicksSync(3);

        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.EqualTo(CFDegree.Success), "No thrall to throw.");
            Assert.That(shielded, Is.EqualTo(CFDegree.Failure));
            Assert.That(CountThralls(hero), Is.EqualTo(0), "The thrall is destroyed.");
        });
    }

    /// <summary>Void Siphon: void damage is resisted by the level, and temporary Hit Points follow once it has landed.</summary>
    [Test]
    public async Task VoidSiphonResistsVoidAndThenShields()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int lost = 0, temp = 0, fireLost = 0;
        await Server.WaitPost(() =>
        {
            var hero = Necromancer();
            GiveFeat(hero, "CFVoidSiphonNecromancer");
            var foe = Spawn("CFNecroFoe", 1);
            Effects.DealDamage(foe, hero, 5, "Void", false);
            lost = 400 - Hp(hero);
            temp = SEntMan.GetComponent<CFCombatantComponent>(hero).TemporaryHp;

            var other = Necromancer();
            GiveFeat(other, "CFVoidSiphonNecromancer");
            Effects.DealDamage(foe, other, 5, "Fire", false);
            fireLost = 400 - Hp(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(lost, Is.EqualTo(4), "Resistance 1 at level 1.");
            Assert.That(temp, Is.EqualTo(1));
            Assert.That(fireLost, Is.EqualTo(5), "Only void.");
        });
    }

    /// <summary>Mastery of Life and Death: Heal hurts a living enemy. Invert Harm: Harm heals a living ally.</summary>
    [Test]
    public async Task MasteryHurtsEnemiesAndInvertHarmHealsAllies()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int hurt = 0, plainHurt = 0, healed = 0;
        await Server.WaitPost(() =>
        {
            var hero = Necromancer();
            GiveFeature(hero, "Mastery of Life and Death");
            GiveFeat(hero, "CFInvertHarm");
            var foe = Spawn("CFNecroFoe", 1);
            Effects.ResolveTarget(SEntMan.SpawnEntity("CFHeal", MapCoordinates.Nullspace), hero, foe);
            hurt = 500 - Hp(foe);

            var friend = Spawn(x: 2);
            Effects.ResolveTarget(SEntMan.SpawnEntity("CFHarm", MapCoordinates.Nullspace), hero, friend);
            healed = Hp(friend) - 400;

            var plain = Necromancer();
            var other = Spawn("CFNecroFoe", 3);
            Effects.ResolveTarget(SEntMan.SpawnEntity("CFHeal", MapCoordinates.Nullspace), plain, other);
            plainHurt = 500 - Hp(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hurt, Is.GreaterThan(0), "Heal deals void to a living enemy.");
            Assert.That(healed, Is.GreaterThan(0), "Harm heals a living ally.");
            Assert.That(plainHurt, Is.EqualTo(0), "Without the mastery, Heal heals the living.");
        });
    }

    /// <summary>Draining Strike: two attacks' worth of penalty, a thrall spent, spirit damage and healing.</summary>
    [Test]
    public async Task DrainingStrikeSpendsAThrallAndHeals()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        int map = 0, healed = 0, hurt = 0;
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            GiveFeat(hero, "CFDrainingStrike");
            var foe = Spawn("CFNecroFoe", 1);
            Raise(hero, 1);
            Actions.PerformAction(hero, Action(hero, "CFActDrainingStrike"), new CFActivityTargetEvent { Target = foe });
            map = SEntMan.GetComponent<CFActionPoolComponent>(hero).MapAttacks;
            healed = Hp(hero) - 400;
            hurt = 500 - Hp(foe);
        });
        await Pair.RunTicksSync(3);

        Assert.Multiple(() =>
        {
            Assert.That(map, Is.EqualTo(2));
            Assert.That(healed, Is.InRange(1, 6));
            Assert.That(hurt, Is.GreaterThan(healed), "The Strike's own damage, and the spirit damage.");
            Assert.That(CountThralls(hero), Is.EqualTo(0));
        });
    }

    /// <summary>Muscle Barrier wraps the caster in 10 temporary Hit Points with +1 to Athletics, and spends the thrall.</summary>
    [Test]
    public async Task MuscleBarrierShieldsTheCaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        int temp = 0, athletics = 0;
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            GiveFeat(hero, "CFMuscleBarrier");
            Raise(hero, 1);
            Effects.ResolveTarget(SEntMan.SpawnEntity("CFMuscleBarrier", MapCoordinates.Nullspace), hero, FirstThrall(hero));
            temp = SEntMan.GetComponent<CFCombatantComponent>(hero).TemporaryHp;
            athletics = SEntMan.System<CFConditionSystem>().SkillBonus(hero, "Athletics");
        });
        await Pair.RunTicksSync(3);

        Assert.Multiple(() =>
        {
            Assert.That(temp, Is.EqualTo(10));
            Assert.That(athletics, Is.EqualTo(1));
            Assert.That(CountThralls(hero), Is.EqualTo(0));
        });
    }

    /// <summary>Song of the Soul heals the hurt ally beside the thrall at once, and the thrall keeps singing to it.</summary>
    [Test]
    public async Task SongOfTheSoulHealsAndKeepsSinging()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, friend = default;
        var healed = 0;
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            GiveFeat(hero, "CFSongoftheSoul");
            SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp = 500;
            friend = Spawn(x: 1);
            Raise(hero, 1);
            Effects.ResolveTarget(SEntMan.SpawnEntity("CFSongOfTheSoul", MapCoordinates.Nullspace), hero, FirstThrall(hero));
            healed = Hp(friend) - 400;
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(healed, Is.InRange(1, 8));
                Assert.That(CountThralls(hero), Is.EqualTo(1), "The thrall survives.");
                Assert.That(SEntMan.GetComponent<CFThrallComponent>(FirstThrall(hero)).SongFor, Is.EqualTo(friend));
            });
        });
    }

    /// <summary>Osteo Armaments puts a +1 striking bone weapon in the hand, and the next use destroys the last one.</summary>
    [Test]
    public async Task OsteoArmamentsMakesABoneWeapon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, first = default;
        var held = "";
        int potency = 0, striking = 0;
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            GiveFeat(hero, "CFOsteoArmaments");
            var action = Action(hero, "CFActOsteoArmaments");
            SEntMan.GetComponent<CFModeChoiceComponent>(action).Chosen = 2;
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            first = SEntMan.GetComponent<CFNecromancerComponent>(hero).BoneWeapon.Value;
            held = SEntMan.GetComponent<MetaDataComponent>(first).EntityPrototype?.ID ?? "";
            var runes = SEntMan.GetComponent<CFWeaponRunesComponent>(first);
            potency = runes.Potency;
            striking = runes.Striking;

            SEntMan.GetComponent<CFModeChoiceComponent>(action).Chosen = 3;
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
        });
        await Pair.RunTicksSync(3);

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EqualTo("CFSpear"));
            Assert.That(potency, Is.EqualTo(1));
            Assert.That(striking, Is.EqualTo(1));
            Assert.That(SEntMan.Deleted(first), "The next use destroys the last weapon.");
        });
    }

    /// <summary>Vampiric Spell: half the most a damaging spell dealt one creature comes back as temporary Hit Points.</summary>
    [Test]
    public async Task VampiricSpellGivesTemporaryHitPoints()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var temp = 0;
        await Server.WaitPost(() =>
        {
            var hero = Necromancer();
            Raise(hero, 1);
            SEntMan.EnsureComponent<CFVampiricPendingComponent>(hero).Until = Timing.CurTime + System.TimeSpan.FromSeconds(10);
            for (var i = 0; i < 6; i++)
            {
                Spawn("CFNecroFoe", 1);
            }

            Effects.ResolveTarget(SEntMan.SpawnEntity("CFNecroticBomb", MapCoordinates.Nullspace), hero, FirstThrall(hero));
            temp = SEntMan.GetComponent<CFCombatantComponent>(hero).TemporaryHp;
        });

        // 1d12 void: any one of six can save for half, but not all of them for nothing.
        Assert.That(temp, Is.GreaterThan(0));
    }

    /// <summary>The Flesh fascination leaves dead flesh where a thrall fell. Mobile Thralls climb. The Hallowed Dead are holy.</summary>
    [Test]
    public async Task ThrallEnhancementsApply()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var climbs = false;
        var holy = false;
        var heroHoly = false;
        await Server.WaitPost(() =>
        {
            var hero = Necromancer("FascinationFlesh");
            GiveFeat(hero, "CFMobileThralls");
            GiveFeat(hero, "CFTheHallowedDead");
            Raise(hero, 1);
            var thrall = FirstThrall(hero);
            climbs = SEntMan.GetComponent<CFCombatOptionsComponent>(thrall).ClimbSpeedUntil == System.TimeSpan.MaxValue;
            holy = SEntMan.GetComponent<CFTraitComponent>(thrall).HasTrait("Holy");
            heroHoly = SEntMan.GetComponent<CFTraitComponent>(hero).HasTrait("Holy");
            SEntMan.DeleteEntity(thrall);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            var flesh = 0;
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (meta.EntityPrototype?.ID == "CFFillFleshTerrain" && xform.MapID == _map.MapId)
                    flesh++;
            }

            Assert.Multiple(() =>
            {
                Assert.That(climbs);
                Assert.That(holy);
                Assert.That(heroHoly);
                Assert.That(flesh, Is.EqualTo(1));
            });
        });
    }

    /// <summary>Overwhelming Harm opens Harm's 60-foot mode, and only for a character with the feat.</summary>
    [Test]
    public async Task OverwhelmingHarmOpensTheWideMode()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Necromancer();
            var harm = SEntMan.SpawnEntity("CFHarm", MapCoordinates.Nullspace);
            var wide = SEntMan.GetComponent<CFModeChoiceComponent>(harm).Modes.First(m => m.Value == 5);
            Assert.That(CFModeRequirements.Met(SEntMan, hero, wide), Is.False);
            GiveFeat(hero, "CFOverwhelmingHarm");
            Assert.That(CFModeRequirements.Met(SEntMan, hero, wide), Is.True);
            Assert.That(wide.AreaFeet, Is.EqualTo(60));
        });
    }

    /// <summary>Every Necromancer feat applies.</summary>
    [Test]
    public async Task EveryNecromancerFeatCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class == "Necromancer"))
            {
                Features.Apply(Spawn(), feat.Grants, feat.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(23));
    }
}
