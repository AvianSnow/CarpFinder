using Content.Server.CF.GOAP.Tactics;
using Content.Shared.CF.GOAP;

// Plain math with no game running, so prototypes are built by hand here.
#pragma warning disable RA0039

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>What the odds and target scores come to. Plain math, so these need no game running.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPOdds))]
public sealed class CFGOAPTacticsMathTest
{
    private static readonly CFGOAPTacticsPrototype Default = new();

    private static CFGOAPTargetView Plain(float tiles = 3f)
    {
        return new CFGOAPTargetView(tiles, 1f, 0.6f, false, false, false, false, false);
    }

    [Test]
    public void ACriticalHitCountsDouble()
    {
        Assert.That(CFGOAPOdds.HitValue(8, 18), Is.EqualTo(0.6f).Within(0.001f), "Ten hits and one critical hit.");
        Assert.That(CFGOAPOdds.HitValue(3, 18), Is.EqualTo(0.35f).Within(0.001f));
        Assert.That(CFGOAPOdds.HitValue(-2, 18), Is.EqualTo(0.1f).Within(0.001f), "Only a natural 20, which then improves.");
        Assert.That(CFGOAPOdds.HitValue(30, 18), Is.EqualTo(1.95f).Within(0.001f), "A natural 1 still costs a step.");
    }

    [Test]
    public void ChanceCountsEverySuccessOnce()
    {
        Assert.That(CFGOAPOdds.Chance(5, 15), Is.EqualTo(0.55f).Within(0.001f));
    }

    /// <summary>Default discipline is 0.25, so these are the two sides of it.</summary>
    [Test]
    public void TheSecondSwingIsWorthTakingAndTheThirdIsNot()
    {
        Assert.That(CFGOAPOdds.StrikeShare(8, -5, 18), Is.GreaterThan(Default.Discipline));
        Assert.That(CFGOAPOdds.StrikeShare(8, -10, 18), Is.LessThan(Default.Discipline));
    }

    [Test]
    public void AnAgileThirdSwingIsWorthTaking()
    {
        Assert.That(CFGOAPOdds.StrikeShare(8, -8, 18), Is.GreaterThan(Default.Discipline));
    }

    [Test]
    public void AnEasyTargetIsWorthSwingingAtAnyway()
    {
        Assert.That(CFGOAPOdds.StrikeShare(8, -10, 5), Is.GreaterThan(Default.Discipline));
    }

    /// <summary>Even a natural 20 misses here. That must not read as a penalty, or it stands holding back for good.</summary>
    [Test]
    public void ATargetItCanNeverHitIsNotAPenalty()
    {
        Assert.That(CFGOAPOdds.HitValue(3, 34), Is.Zero);
        Assert.That(CFGOAPOdds.StrikeShare(3, 0, 34), Is.EqualTo(1f));
    }

    [Test]
    public void AHigherBonusAddsMore()
    {
        Assert.That(CFGOAPOdds.BonusShare(8, 2, 18), Is.GreaterThan(CFGOAPOdds.BonusShare(8, 1, 18)));
        Assert.That(CFGOAPOdds.BonusShare(8, 1, 18), Is.GreaterThan(0f));
    }

    [Test]
    public void InsightBlendsFromAnEvenFightToTheRealOne()
    {
        Assert.That(CFGOAPOdds.JudgedDc(8, 30, 0f), Is.EqualTo(18));
        Assert.That(CFGOAPOdds.JudgedDc(8, 30, 0.5f), Is.EqualTo(24));
        Assert.That(CFGOAPOdds.JudgedDc(8, 30, 1f), Is.EqualTo(30));
    }

    [Test]
    public void TheNearerOfTwoEqualTargetsWins()
    {
        Assert.That(CFGOAPTargetScore.Score(Default, Plain(2f)), Is.GreaterThan(CFGOAPTargetScore.Score(Default, Plain(5f))));
    }

    [Test]
    public void OneHitFromDownBeatsANearerFreshOne()
    {
        var dropping = Plain(6f) with { Health = 0.1f, CouldDrop = true };
        Assert.That(CFGOAPTargetScore.Score(Default, dropping), Is.GreaterThan(CFGOAPTargetScore.Score(Default, Plain(1f))));
    }

    [Test]
    public void ItStaysOnItsTargetOverOneSlightlyNearer()
    {
        var current = Plain(3f) with { IsCurrent = true };
        Assert.That(CFGOAPTargetScore.Score(Default, current), Is.GreaterThan(CFGOAPTargetScore.Score(Default, Plain(2f))));
    }

    [Test]
    public void ATacticianGoesPastACompanionToTheCaster()
    {
        var tactician = new CFGOAPTacticsPrototype { Casters = 1f, Companions = -0.5f };
        var pet = Plain(1f) with { IsCompanion = true };
        var caster = Plain(5f) with { IsCaster = true };
        Assert.That(CFGOAPTargetScore.Score(tactician, caster), Is.GreaterThan(CFGOAPTargetScore.Score(tactician, pet)));
    }
}
