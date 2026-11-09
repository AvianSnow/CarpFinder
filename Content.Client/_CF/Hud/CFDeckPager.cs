using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CF.Hud;

/// <summary>◀ 1/2 ▶ under a section. It keeps its height with one page, so the sections stay level.</summary>
public sealed class CFDeckPager : BoxContainer
{
    private const string IconFontPath = "/Fonts/_CF/FontAwesome/fa-solid-900.ttf";
    private const char IconLeft = '';
    private const char IconRight = '';

    private static Font? _icons;

    private readonly ContainerButton _previous;
    private readonly ContainerButton _next;
    private readonly Label _label = new();
    private int _count = 1;

    public int Page { get; private set; }

    /// <summary>The page was changed by a click.</summary>
    public event Action<int>? PageChanged;

    public CFDeckPager()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 3;
        MinHeight = 18f;
        HorizontalAlignment = HAlignment.Center;
        Align = AlignMode.Center;

        _icons ??= new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>(IconFontPath), 10);
        _previous = Arrow(IconLeft, -1);
        _next = Arrow(IconRight, 1);
        AddChild(_previous);
        AddChild(_label);
        AddChild(_next);
        SetCount(1);
    }

    /// <summary>How many pages there are. The current page moves back if it no longer exists.</summary>
    public void SetCount(int count)
    {
        _count = Math.Max(1, count);
        Page = Math.Clamp(Page, 0, _count - 1);

        var many = _count > 1;
        _previous.Visible = many;
        _next.Visible = many;
        _label.Visible = many;
        _label.Text = $"{Page + 1}/{_count}";
    }

    private ContainerButton Arrow(char glyph, int direction)
    {
        var button = new ContainerButton { MinSize = new Vector2(16f, 16f) };
        button.AddChild(new Label
        {
            Text = glyph.ToString(),
            FontOverride = _icons,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        });
        button.OnPressed += _ => Turn(direction);
        return button;
    }

    private void Turn(int direction)
    {
        // Wraps round, so one arrow can reach every page.
        Page = (Page + direction + _count) % _count;
        SetCount(_count);
        PageChanged?.Invoke(Page);
    }
}
