using System.Collections.Generic;
using Content.Client.CF.Hud;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Spells;

namespace Content.IntegrationTests.Tests.CF.Hud;

/// <summary>The HUD deck's rules, with no game running.</summary>
[TestFixture]
[TestOf(typeof(CFDeckDrawerState))]
public sealed class CFDeckLogicTest
{
    private static CFDeckDrawerState OpenAndLeft()
    {
        var drawer = new CFDeckDrawerState();
        drawer.Update(0.1f, overTab: true, overFrame: true);
        drawer.Update(0.1f, overTab: false, overFrame: false);
        return drawer;
    }

    [Test]
    public void HoveringTheTabOpensTheDeck()
    {
        var drawer = new CFDeckDrawerState();
        drawer.Update(0.1f, overTab: true, overFrame: true);
        Assert.That(drawer.Expanded);
    }

    [Test]
    public void AClickJustAfterHoverDoesNotCloseIt()
    {
        var drawer = new CFDeckDrawerState();
        drawer.Update(0.1f, overTab: true, overFrame: true);
        drawer.Press();
        Assert.That(drawer.Expanded, "The click landed as hover opened it.");

        drawer.Update(1f, overTab: true, overFrame: true);
        drawer.Press();
        Assert.That(drawer.Expanded, Is.False, "A deliberate click later closes it.");
    }

    [Test]
    public void ItStaysClosedAfterAClickWhileTheMouseRestsOnTheTab()
    {
        var drawer = new CFDeckDrawerState();
        drawer.Update(0.1f, overTab: true, overFrame: true);
        drawer.Update(1f, overTab: true, overFrame: true);
        drawer.Press();
        drawer.Update(1f, overTab: true, overFrame: true);
        Assert.That(drawer.Expanded, Is.False);
    }

    [Test]
    public void ItClosesFiveSecondsAfterTheMouseLeaves()
    {
        var drawer = OpenAndLeft();
        drawer.Update(4.8f, overTab: false, overFrame: false);
        Assert.That(drawer.Expanded, "Not yet.");
        drawer.Update(0.3f, overTab: false, overFrame: false);
        Assert.That(drawer.Expanded, Is.False);
    }

    [Test]
    public void ComingBackRestartsTheFiveSeconds()
    {
        var drawer = OpenAndLeft();
        drawer.Update(4f, overTab: false, overFrame: false);
        drawer.Update(0.1f, overTab: false, overFrame: true);
        drawer.Update(4f, overTab: false, overFrame: false);
        Assert.That(drawer.Expanded);
    }

    [Test]
    public void APinnedDeckStaysOpen()
    {
        var drawer = OpenAndLeft();
        drawer.Pinned = true;
        drawer.Update(60f, overTab: false, overFrame: false);
        Assert.That(drawer.Expanded);
    }

    [Test]
    public void ADividerTradesColumnsBetweenItsNeighboursOnly()
    {
        var widths = new CFDeckWidths();
        var (first, second, third) = (widths[0], widths[1], widths[2]);

        Assert.That(widths.Move(0, 1), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(widths[0], Is.EqualTo(first + 1));
            Assert.That(widths[1], Is.EqualTo(second - 1));
            Assert.That(widths[2], Is.EqualTo(third));
        });
    }

    [Test]
    public void NoSectionCanBeCrushed()
    {
        var widths = new CFDeckWidths();
        var right = widths[1];

        Assert.That(widths.Move(0, 50), Is.EqualTo(right - CFDeckWidths.MinColumns), "It moves as far as it can.");
        Assert.That(widths[1], Is.EqualTo(CFDeckWidths.MinColumns));

        var left = widths[0];
        Assert.That(widths.Move(0, -50), Is.EqualTo(CFDeckWidths.MinColumns - left));
        Assert.That(widths[0], Is.EqualTo(CFDeckWidths.MinColumns));
    }

    [Test]
    public void ColumnsSurviveASaveAndBadSavesAreIgnored()
    {
        var widths = new CFDeckWidths();
        widths.Move(1, 1);
        Assert.That(CFDeckWidths.Parse(widths.Save()).Save(), Is.EqualTo(widths.Save()));

        var defaults = new CFDeckWidths().Save();
        Assert.Multiple(() =>
        {
            Assert.That(CFDeckWidths.Parse("rubbish").Save(), Is.EqualTo(defaults));
            Assert.That(CFDeckWidths.Parse("5,5").Save(), Is.EqualTo(defaults), "Too few sections.");
            Assert.That(CFDeckWidths.Parse("3,4,1,2").Save(), Is.EqualTo(defaults), "The old Items section is gone.");
            Assert.That(CFDeckWidths.Parse("5,5,5").Save(), Is.EqualTo(defaults), "More columns than the deck has.");
            Assert.That(CFDeckWidths.Parse("0,5,5").Save(), Is.EqualTo(defaults), "A section with no columns.");
        });
    }

    [Test]
    public void SlotsFlowOntoLaterPages()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFDeckLayout.PageCount(0, 2, 2), Is.EqualTo(1), "An empty section still has a page.");
            Assert.That(CFDeckLayout.PageCount(4, 2, 2), Is.EqualTo(1));
            Assert.That(CFDeckLayout.PageCount(5, 2, 2), Is.EqualTo(2));
            Assert.That(CFDeckLayout.Position(3, 2, 2, 0), Is.EqualTo((1, 1)));
            Assert.That(CFDeckLayout.Position(4, 2, 2, 0), Is.Null, "It is on the next page.");
            Assert.That(CFDeckLayout.Position(4, 2, 2, 1), Is.EqualTo((0, 0)));
        });
    }

    private static CFSpellbookComponent PreparedBook()
    {
        var book = new CFSpellbookComponent { Prepared = true };
        book.Slots.AddRange(new[] { 5, 3 });
        book.Expended.AddRange(new[] { 0, 0 });
        book.PreparableSlots.AddRange(new[] { 5, 3 });
        book.Repertoire.Add("Fear");
        book.Repertoire.Add("Bless");
        book.PreparedSlots.Add(new CFPreparedSlot("Fear", 1));
        book.PreparedSlots.Add(new CFPreparedSlot("Fear", 1));
        book.PreparedSlots.Add(new CFPreparedSlot("Bless", 1));
        return book;
    }

    [Test]
    public void EachCastSpendsOneChargeOfThatSpellOnly()
    {
        var book = PreparedBook();
        Assert.That(CFPreparedSpells.Charges(book, "Fear"), Is.EqualTo((2, 2)));

        CFPreparedSpells.Spend(book, "Fear", 1);
        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.Charges(book, "Fear"), Is.EqualTo((1, 2)));
            Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((1, 1)));
            Assert.That(book.Expended[1], Is.EqualTo(1));
        });

        CFPreparedSpells.Spend(book, "Fear", 1);
        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.CanCast(book, "Fear", 1), Is.False);
            Assert.That(CFPreparedSpells.CanCast(book, "Bless", 1), "A different spell keeps its charge.");
        });
    }

    [Test]
    public void AKnownSpellThatIsNotPreparedCannotBeCast()
    {
        var book = PreparedBook();
        book.Repertoire.Add("Charm");
        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.CanCast(book, "Charm", 1), Is.False);
            Assert.That(CFPreparedSpells.UsesCharges(book, "Charm"), "It shows x0 rather than nothing.");
        });
    }

    [Test]
    public void ASpellFromAFeatIsAtWill()
    {
        var book = PreparedBook();
        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.CanCast(book, "Sleep", 1));
            Assert.That(CFPreparedSpells.UsesCharges(book, "Sleep"), Is.False);
        });

        CFPreparedSpells.Spend(book, "Sleep", 1);
        Assert.That(book.Expended[1], Is.Zero, "It costs nothing.");
    }

    [Test]
    public void OnlyFontSpellsCanUseFontSlots()
    {
        var book = PreparedBook();
        book.Slots[1] += 2;
        book.SpareSlotSpells.Add("Heal");
        for (var i = 0; i < 3; i++)
            CFPreparedSpells.Spend(book, i < 2 ? "Fear" : "Bless", 1);

        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.SpareLeft(book, 1), Is.EqualTo(2));
            Assert.That(CFPreparedSpells.CanCast(book, "Bless", 1), Is.False, "Bless is out of charges, and font slots are not for it.");
            Assert.That(CFPreparedSpells.CanCast(book, "Heal", 1));
        });

        CFPreparedSpells.Spend(book, "Heal", 1);
        CFPreparedSpells.Spend(book, "Heal", 1);
        Assert.That(CFPreparedSpells.CanCast(book, "Heal", 1), Is.False);
    }

    [Test]
    public void ALongRestPreparesThePlanWithEveryChargeReady()
    {
        var book = PreparedBook();
        CFPreparedSpells.Spend(book, "Fear", 1);
        book.PlannedSlots.Add(new CFPreparedSlot("Bless", 1));
        book.PlannedSlots.Add(new CFPreparedSlot("Bless", 1));

        CFPreparedSpells.Refresh(book);
        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.Charges(book, "Bless"), Is.EqualTo((2, 2)));
            Assert.That(CFPreparedSpells.Charges(book, "Fear"), Is.EqualTo((0, 0)));
        });
    }

    [Test]
    public void APlanMustFitTheSlotsAndKnownSpells()
    {
        var book = PreparedBook();
        var fits = new List<CFPreparedSlot> { new("Fear", 1), new("Fear", 1), new("Bless", 1) };
        var tooMany = new List<CFPreparedSlot> { new("Fear", 1), new("Fear", 1), new("Fear", 1), new("Bless", 1) };
        var unknown = new List<CFPreparedSlot> { new("Fireball", 1) };
        var cantrip = new List<CFPreparedSlot> { new("Fear", 0) };
        var beyond = new List<CFPreparedSlot> { new("Fear", 9) };

        Assert.Multiple(() =>
        {
            Assert.That(CFPreparedSpells.IsValidPlan(book, fits));
            Assert.That(CFPreparedSpells.IsValidPlan(book, tooMany), Is.False);
            Assert.That(CFPreparedSpells.IsValidPlan(book, unknown), Is.False);
            Assert.That(CFPreparedSpells.IsValidPlan(book, cantrip), Is.False);
            Assert.That(CFPreparedSpells.IsValidPlan(book, beyond), Is.False);
        });
    }

    [Test]
    public void ActionsAreSortedIntoTheirTabs()
    {
        var common = new CFRadialSlotComponent { Tab = CFDeckSection.Common };
        var feat = new CFRadialSlotComponent();
        var refocus = new CFRadialSlotComponent { Group = CFRadialGroup.Free, Tab = CFDeckSection.Focus };
        var reaction = new CFRadialSlotComponent { Group = CFRadialGroup.Reaction };

        Assert.Multiple(() =>
        {
            Assert.That(CFDeckSorter.Place(common, null), Is.EqualTo(new CFDeckPlace(CFDeckTab.Common)));
            Assert.That(CFDeckSorter.Place(feat, null), Is.EqualTo(new CFDeckPlace(CFDeckTab.ClassFeatures)));
            Assert.That(CFDeckSorter.Place(refocus, null), Is.EqualTo(new CFDeckPlace(CFDeckTab.Spells, CFDeckSorter.FocusGroup)));
            Assert.That(CFDeckSorter.Place(reaction, null), Is.Null);
            Assert.That(CFDeckSorter.Place(null, null), Is.Null, "Stock actions only live on Custom.");
            Assert.That(CFDeckSorter.Place(feat, new CFSpellComponent { Rank = 2 }), Is.EqualTo(new CFDeckPlace(CFDeckTab.Spells, 2)));
            Assert.That(CFDeckSorter.Place(feat, new CFSpellComponent()), Is.EqualTo(new CFDeckPlace(CFDeckTab.Spells, 0)));
            Assert.That(CFDeckSorter.Place(feat, new CFSpellComponent { Focus = true }), Is.EqualTo(new CFDeckPlace(CFDeckTab.Spells, CFDeckSorter.FocusGroup)));
        });
    }
}
