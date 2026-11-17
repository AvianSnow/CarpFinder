using Robust.Shared.Prototypes;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Character;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Companions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Summon Animal: which creature comes, whose side it is on, and when it goes.</summary>
[TestFixture]
[TestOf(typeof(CFSummonedSystem))]
public sealed class CFSummonTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSummonTestCaster
  components:
  - type: CFCombatant
    level: 5
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFSummonTestCaster
  id: CFSummonTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    /// <summary>Runs the spell's effects at a rank, as casting it from a slot of that rank would.</summary>
    private void Cast(EntityUid caster, int rank)
    {
        var spell = SEntMan.SpawnEntity("CFSummonAnimal", MapCoordinates.Nullspace);
        SEntMan.System<CFEffectSystem>().Run(SEntMan.GetComponent<CFDegreeEffectsComponent>(spell).Landed, new CFEffectArgs
        {
            User = caster,
            Source = spell,
            Position = _map.Local(1, 0),
            Rank = rank,
            Degree = CFDegree.Success,
            Defense = CFDefenseType.None,
        });
    }

    private EntityUid? SummonOf(EntityUid caster)
    {
        var query = SEntMan.AllEntityQueryEnumerator<CFSummonedComponent, CFCompanionComponent>();
        while (query.MoveNext(out var uid, out _, out var companion))
        {
            if (companion.Summoner == caster && !SEntMan.Deleted(uid) && SEntMan.GetComponent<MetaDataComponent>(uid).EntityLifeStage < EntityLifeStage.Terminating)
                return uid;
        }

        return null;
    }

    private string Id(EntityUid? uid) => uid is { } id ? SEntMan.GetComponent<MetaDataComponent>(id).EntityPrototype?.ID : null;

    [Test]
    public async Task TheRankPicksTheAnimalAndItFightsForItsCaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid caster = default, first = default;
        string atOne = null;
        bool harmsCaster = true, casterHarms = true, harmsFoe = false, summonedTrait = false;
        await Server.WaitPost(() =>
        {
            var factions = SEntMan.System<CFFactionSystem>();
            caster = SEntMan.SpawnEntity("CFSummonTestCaster", _map.Local(0, 0));
            var foe = SEntMan.SpawnEntity("CFSummonTestFoe", _map.Local(2, 0));
            Cast(caster, 1);
            first = SummonOf(caster)!.Value;
            atOne = Id(first);
            harmsCaster = factions.CanHarm(first, caster);
            casterHarms = factions.CanHarm(caster, first);
            harmsFoe = factions.CanHarm(first, foe);
            summonedTrait = SEntMan.GetComponent<CFTraitComponent>(first).Traits.Contains("Summoned");
        });

        // A second cast calls a second creature: nothing but a dispel sends the first back early.
        EntityUid second = default;
        string atTwo = null;
        var firstStays = false;
        await Server.WaitPost(() =>
        {
            Cast(caster, 2);
            var query = SEntMan.AllEntityQueryEnumerator<CFSummonedComponent, CFCompanionComponent>();
            while (query.MoveNext(out var uid, out _, out var companion))
            {
                if (companion.Summoner == caster && uid != first)
                    second = uid;
            }

            atTwo = Id(second);
        });
        await Pair.RunTicksSync(5);

        var dispelled = false;
        await Server.WaitPost(() =>
        {
            firstStays = !SEntMan.Deleted(first);
            dispelled = SEntMan.System<CFCounteractSystem>().TryDispel(caster, first, 3, 50, d20: 20);
            SEntMan.GetComponent<CFSummonedComponent>(second).Until = SGameTiming.CurTime;
        });
        await Pair.RunTicksSync(5);

        bool expired = false, firstGone = false;
        await Server.WaitPost(() =>
        {
            expired = SEntMan.Deleted(second);
            firstGone = SEntMan.Deleted(first);
        });

        Assert.Multiple(() =>
        {
            Assert.That(atOne, Is.EqualTo("CFSummonedGiantRat"), "Rank 1 calls the level -1 animal.");
            Assert.That(harmsCaster, Is.False, "It is on its caster's side.");
            Assert.That(casterHarms, Is.False);
            Assert.That(harmsFoe, Is.True, "And against the caster's enemies.");
            Assert.That(summonedTrait, Is.True);
            Assert.That(atTwo, Is.EqualTo("CFSummonedWolf"), "Rank 2 calls the level 1 animal.");
            Assert.That(firstStays, Is.True, "The first stays when the spell is cast again.");
            Assert.That(dispelled, Is.True);
            Assert.That(firstGone, Is.True, "Dispel Magic sends it back.");
            Assert.That(expired, Is.True, "It goes back when its time is up.");
        });
    }

    /// <summary>Nobody steers it: the wolf has to find the caster's enemy and bite it by itself.</summary>
    [Test]
    public async Task ASummonedWolfAttacksTheCastersEnemy()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid caster = default, foe = default;
        await Server.WaitPost(() =>
        {
            caster = SEntMan.SpawnEntity("CFSummonTestCaster", _map.Local(0, 0));
            foe = SEntMan.SpawnEntity("CFSummonTestFoe", _map.Local(3, 0));
        });

        // The path graph needs a moment after the map loads.
        await Pair.RunSeconds(3f);
        await Server.WaitPost(() => Cast(caster, 2));
        await Pair.RunSeconds(20f);

        int foeHp = 0, casterHp = 0;
        await Server.WaitPost(() =>
        {
            foeHp = SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
            casterHp = SEntMan.GetComponent<CFCombatantComponent>(caster).CurrentHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(foeHp, Is.LessThan(50), "The wolf went for the enemy.");
            Assert.That(casterHp, Is.EqualTo(50), "And left its caster alone.");
        });
    }

    /// <summary>Call of the Wild: the summon takes the place of a prepared spell, and is not free.</summary>
    [Test]
    public async Task CallOfTheWildSpendsAPreparedSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = false, after = true, known = false;
        int rank = 0, fearLeft = -1;
        await Server.WaitPost(() =>
        {
            var druid = SEntMan.SpawnEntity("CFSummonTestCaster", _map.Local(0, 0));
            var book = SEntMan.EnsureComponent<CFSpellbookComponent>(druid);
            book.Prepared = true;
            book.Slots = new List<int> { 5, 1, 1 };
            book.Expended = new List<int> { 0, 0, 0 };
            book.Repertoire.Add("Fear");
            book.PreparedSlots.Add(new CFPreparedSlot("Fear", 2));

            var feat = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFCalloftheWild"));
            SEntMan.System<CFCharacterFeatureSystem>().Apply(druid, feat.Grants, "feat:CFCalloftheWild");
            known = book.SwapSpells.Contains("SummonAnimal");

            before = CFPreparedSpells.CanCast(book, "SummonAnimal", 1);
            rank = CFPreparedSpells.Spend(book, "SummonAnimal", 1);
            fearLeft = CFPreparedSpells.Charges(book, "Fear").Ready;
            after = CFPreparedSpells.CanCast(book, "SummonAnimal", 1);
        });

        Assert.Multiple(() =>
        {
            Assert.That(known, Is.True);
            Assert.That(before, Is.True);
            Assert.That(rank, Is.EqualTo(2), "It is cast at the rank of the spell it replaces.");
            Assert.That(fearLeft, Is.Zero, "The prepared spell is spent.");
            Assert.That(after, Is.False, "With nothing left prepared, it cannot be cast.");
        });
    }

    /// <summary>Floating Flame is sustained, and casting it again while it burns moves it and costs no second cast.</summary>
    [Test]
    public async Task FloatingFlameCanBeMovedWhileItBurns()
    {
        _map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 7, 2), avoidStructures: true);
        EntityUid caster = default, far = default;
        var casts = 0;
        var moved = false;
        await Server.WaitPost(() =>
        {
            caster = SEntMan.SpawnEntity("CFSummonTestCaster", _map.Local(0, 0));
            far = SEntMan.SpawnEntity("CFSummonTestFoe", _map.Local(5, 0));
            var flame = SEntMan.SpawnEntity("CFFloatingFlame", MapCoordinates.Nullspace);

            SEntMan.EventBus.RaiseLocalEvent(flame, new CFSpellCastAreaEvent { Performer = caster, Target = _map.Local(1, 0) });
            SEntMan.EventBus.RaiseLocalEvent(flame, new CFSpellCastAreaEvent { Performer = caster, Target = _map.Local(5, 0) });

            var running = SEntMan.GetComponent<CFSustainedCastsComponent>(caster).Casts;
            casts = running.Count;
            var xform = SEntMan.System<SharedTransformSystem>();
            moved = running.Count == 1
                    && running[0].Aim is { } aim
                    && (aim.Position - xform.ToMapCoordinates(_map.Local(5, 0)).Position).Length() < 0.1f;
        });

        // Four pulses at the new spot. The creature there starts out of the first burst's reach.
        await Pair.RunSeconds(25f);
        var hp = 0;
        await Server.WaitPost(() =>
        {
            hp = SEntMan.GetComponent<CFCombatantComponent>(far).CurrentHp;

            // The server is shared with other tests, and the flame would burn on into them.
            SEntMan.RemoveComponent<CFSustainedCastsComponent>(caster);
        });
        await Pair.RunSeconds(2f);

        Assert.Multiple(() =>
        {
            Assert.That(casts, Is.EqualTo(1), "The second cast moved the flame. It did not start another.");
            Assert.That(moved, Is.True);
            Assert.That(hp, Is.LessThan(50), "It burns where it was moved to.");
        });
    }
}
