using System.Numerics;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.CF.Hud;

/// <summary>
/// The numbered hotbar: the deck's Custom section. Slots 1 to 10 are keys 1–0, and 11 to 20 use the same numbers with
/// whatever modifier the player bound, where the label shows only the key. Later slots have no key.
/// </summary>
public sealed class CFActionGrid : ActionButtonContainer
{
    private const int ColumnCount = CFDeckLayout.TotalColumns;

    /// <summary>The most rows the plus button can add.</summary>
    public const int MaxRows = CFDeckLayout.MaxRows;

    private readonly IInputManager _keys;
    private int _rows = 2;
    private int _columns = ColumnCount;
    private int _page;

    public CFActionGrid()
    {
        MinimumButtons = ColumnCount * MaxRows;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        _keys = IoCManager.Resolve<IInputManager>();
        _keys.OnKeyBindingAdded += OnKeybindsChanged;
        _keys.OnKeyBindingRemoved += OnKeybindsChanged;
    }

    /// <summary>How many rows are on show. The rest stay assigned, and their keys still work.</summary>
    public int VisibleRows
    {
        get => _rows;
        set
        {
            _rows = Math.Clamp(value, 1, MaxRows);
            InvalidateMeasure();
        }
    }

    /// <summary>How many columns are on show, which the deck's dividers change.</summary>
    public int VisibleColumns
    {
        get => _columns;
        set
        {
            _columns = Math.Clamp(value, 1, ColumnCount);
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

    /// <summary>Pages reach one slot past the last action, so there is always somewhere to drop a new one.</summary>
    public int SlotsInUse()
    {
        var last = -1;
        for (var i = 0; i < ChildCount; i++)
        {
            if (GetChild(i) is ActionButton { Action: not null })
                last = i;
        }

        return Math.Min(ChildCount, last + 2);
    }

    protected override string FormatKeyLabel(int index, IKeyBinding binding)
    {
        var text = binding.GetKeyString();
        if (index < ColumnCount)
            return text;

        var plus = text.LastIndexOf('+');
        return plus >= 0 && plus < text.Length - 1 ? text[(plus + 1)..] : text;
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

    private void OnKeybindsChanged(IKeyBinding _)
    {
        var keys = ContentKeyFunctions.GetHotbarBoundKeys();
        for (var i = 0; i < ChildCount && i < keys.Length; i++)
        {
            if (GetChild(i) is not ActionButton button)
                continue;

            if (_keys.TryGetKeyBinding(keys[i], out var binding))
                button.Label.Text = FormatKeyLabel(i, binding);
        }
    }
}
