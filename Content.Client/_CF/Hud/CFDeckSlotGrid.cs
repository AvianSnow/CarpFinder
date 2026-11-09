using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Hud;

/// <summary>A page of fixed-size slots, for one section of the deck.</summary>
public sealed class CFDeckSlotGrid : Control
{
    private int _columns = 1;
    private int _rows = 1;
    private int _page;

    public CFDeckSlotGrid()
    {
        RectClipContent = true;
    }

    public int SlotColumns
    {
        get => _columns;
        set
        {
            _columns = Math.Max(1, value);
            InvalidateMeasure();
        }
    }

    public int SlotRows
    {
        get => _rows;
        set
        {
            _rows = Math.Max(1, value);
            InvalidateMeasure();
        }
    }

    public int Page
    {
        get => _page;
        set
        {
            if (_page == value)
                return;

            _page = value;
            InvalidateArrange();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Vector2(CFDeckLayout.Slot, CFDeckLayout.Slot));

        return new Vector2(CFDeckLayout.Width(_columns), CFDeckLayout.Height(_rows));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        CFDeckLayout.Arrange(this, _columns, _rows, _page);
        return finalSize;
    }
}
