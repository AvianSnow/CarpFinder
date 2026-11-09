using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Hud;

/// <summary>Where fixed-size slots go in a section of the deck, and how many pages they need.</summary>
public static class CFDeckLayout
{
    public const float Slot = 64f;
    public const float Separation = 2f;

    /// <summary>The columns of the whole deck.</summary>
    public const int TotalColumns = 10;

    public const int MaxRows = 4;

    public static float Width(int columns)
    {
        return columns * Slot + (columns - 1) * Separation;
    }

    public static float Height(int rows)
    {
        return rows * Slot + (rows - 1) * Separation;
    }

    /// <summary>At least one page, even with nothing to show.</summary>
    public static int PageCount(int slots, int columns, int rows)
    {
        var perPage = Math.Max(1, columns * rows);
        return Math.Max(1, (slots + perPage - 1) / perPage);
    }

    /// <summary>A slot's column and row, or null when it is on another page.</summary>
    public static (int Column, int Row)? Position(int index, int columns, int rows, int page)
    {
        var perPage = Math.Max(1, columns * rows);
        if (index / perPage != page)
            return null;

        var local = index % perPage;
        return (local % columns, local / columns);
    }

    /// <summary>Arranges a control's children as slots. Those on other pages go outside it, where it clips them.</summary>
    public static void Arrange(Control parent, int columns, int rows, int page)
    {
        for (var i = 0; i < parent.ChildCount; i++)
        {
            var place = Position(i, columns, rows, page);
            var at = place is { } found
                ? new Vector2(found.Column * (Slot + Separation), found.Row * (Slot + Separation))
                : new Vector2(-2f * Slot, -2f * Slot);
            parent.GetChild(i).Arrange(UIBox2.FromDimensions(at.X, at.Y, Slot, Slot));
        }
    }
}
