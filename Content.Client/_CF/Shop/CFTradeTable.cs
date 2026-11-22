using System.Numerics;
using Content.Shared.CF.Shop;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CF.Shop;

/// <summary>
/// Rows for the counter windows, laid out like the shop's: icon, name, description, then fixed columns.
/// </summary>
public static class CFTradeTable
{
    public const int IconWidth = 40;
    public const int CostWidth = 80;
    public const int ButtonWidth = 80;

    /// <summary>The header row. <paramref name="columns"/> are the fixed columns after the description.</summary>
    public static Control Header(params (string Text, int Width)[] columns)
    {
        var row = Row();
        row.AddChild(Fixed(new Label { Text = Loc.GetString("cf-shop-col-icon"), Align = Label.AlignMode.Center }, IconWidth));
        row.AddChild(new Label { Text = Loc.GetString("cf-shop-col-name"), MinWidth = 120, HorizontalExpand = true, ClipText = true });
        row.AddChild(new Label { Text = Loc.GetString("cf-shop-col-description"), HorizontalExpand = true, ClipText = true, SizeFlagsStretchRatio = 2 });
        foreach (var (text, width) in columns)
        {
            row.AddChild(Fixed(new Label { Text = text, Align = Label.AlignMode.Center }, width));
        }

        var panel = new PanelContainer { HorizontalExpand = true, RectClipContent = true, StyleClasses = { "BackgroundDark" } };
        panel.AddChild(row);
        return panel;
    }

    /// <summary>One item: its icon, name and description, then <paramref name="columns"/>.</summary>
    public static Control Item(CFCounterEntry entry, params Control[] columns)
    {
        var icon = new EntityPrototypeView
        {
            MinSize = new Vector2(32, 32),
            Stretch = SpriteView.StretchMode.Fit,
        };
        if (entry.Prototype.Length > 0)
            icon.SetPrototype(entry.Prototype);

        var row = Row();
        row.AddChild(Fixed(icon, IconWidth));
        row.AddChild(new Label
        {
            Text = entry.Name,
            MinWidth = 120,
            HorizontalExpand = true,
            ClipText = true,
            FontColorOverride = Color.FromHex("#E6C15A"),
        });
        row.AddChild(new Label
        {
            Text = entry.Description,
            HorizontalExpand = true,
            ClipText = true,
            SizeFlagsStretchRatio = 2,
            ToolTip = entry.Description,
        });
        foreach (var column in columns)
        {
            row.AddChild(column);
        }

        var panel = new PanelContainer { HorizontalExpand = true, RectClipContent = true };
        panel.AddChild(row);
        return panel;
    }

    /// <summary>A fixed-width text cell.</summary>
    public static Control Cell(string text, int width, Label.AlignMode align = Label.AlignMode.Right)
    {
        return Fixed(new Label { Text = text, ClipText = true, Align = align }, width);
    }

    /// <summary>A fixed-width button.</summary>
    public static Button Button(string text, bool disabled, Action pressed)
    {
        var button = new Button { Text = text, ClipText = true, Disabled = disabled };
        Fixed(button, ButtonWidth);
        button.OnPressed += _ => pressed();
        return button;
    }

    /// <summary>The price an entry fetches, or "not wanted".</summary>
    public static string Price(int copper)
    {
        return copper > 0 ? CFCoins.FormatPrice(copper) : Loc.GetString("cf-merchant-no-value");
    }

    private static T Fixed<T>(T control, int width) where T : Control
    {
        control.SetWidth = width;
        control.MinWidth = width;
        control.MaxWidth = width;
        control.HorizontalExpand = false;
        return control;
    }

    private static BoxContainer Row()
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            RectClipContent = true,
            Margin = new Thickness(4),
            SeparationOverride = 8,
        };
    }
}
