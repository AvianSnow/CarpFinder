using System.Numerics;
using Content.Client.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Spells;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CF.Hud;

/// <summary>
/// The Spells tab's trackers. A spontaneous caster sees boxes and a count for each rank it has slots in, and clicking one
/// goes to that rank. Anyone with a focus pool sees its gems. Prepared casters read their charges on the spells themselves.
/// </summary>
public sealed class CFSpellPips : BoxContainer
{
    private static readonly Color GemReady = new(150, 110, 230);
    private static readonly Color GemSpent = new(72, 72, 76);

    private readonly BoxContainer _ranks = new()
    {
        Orientation = LayoutOrientation.Horizontal,
        SeparationOverride = 12,
        HorizontalExpand = true,
    };

    private readonly BoxContainer _focus = new()
    {
        Orientation = LayoutOrientation.Horizontal,
        SeparationOverride = 2,
    };

    private string _shown = string.Empty;

    /// <summary>A rank's boxes were clicked.</summary>
    public event Action<int>? RankPressed;

    public CFSpellPips()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 8;
        AddChild(_ranks);
        AddChild(_focus);
    }

    public void SetBook(CFSpellbookComponent? book)
    {
        var key = Key(book);
        if (key == _shown)
            return;

        _shown = key;
        _ranks.RemoveAllChildren();
        _focus.RemoveAllChildren();
        if (book == null)
        {
            Visible = false;
            return;
        }

        if (!book.Prepared)
        {
            for (var rank = 1; rank < book.Slots.Count; rank++)
            {
                if (book.Slots[rank] > 0)
                    _ranks.AddChild(RankBoxes(rank, book.Slots[rank], rank < book.Expended.Count ? book.Expended[rank] : 0));
            }
        }

        if (book.FocusPoints > 0)
            AddGems(book);

        Visible = _ranks.ChildCount > 0 || _focus.ChildCount > 0;
    }

    private void AddGems(CFSpellbookComponent book)
    {
        var tooltip = Loc.GetString("cf-hud-focus-tooltip", ("ready", CFFocusSystem.Remaining(book)), ("max", book.FocusPoints));
        _focus.AddChild(new Label { Text = Loc.GetString("cf-hud-focus"), ToolTip = tooltip, MouseFilter = MouseFilterMode.Stop, Margin = new Thickness(0, 0, 4, 0) });
        for (var i = 0; i < book.FocusPoints; i++)
        {
            _focus.AddChild(new Label
            {
                Text = "◆",
                FontColorOverride = i < CFFocusSystem.Remaining(book) ? GemReady : GemSpent,
                ToolTip = tooltip,
                MouseFilter = MouseFilterMode.Stop,
            });
        }
    }

    private ContainerButton RankBoxes(int rank, int count, int spent)
    {
        var ready = Math.Max(0, count - Math.Max(0, spent));
        var button = new ContainerButton { ToolTip = CFSpellsTab.RankTitle(rank) };
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 2 };
        row.AddChild(new Label { Text = Loc.GetString("cf-hud-rank-tracker", ("rank", rank)), Margin = new Thickness(0, 0, 3, 0) });
        for (var i = 0; i < count; i++)
        {
            row.AddChild(new PanelContainer
            {
                MinSize = new Vector2(12, 12),
                VerticalAlignment = VAlignment.Center,
                PanelOverride = Box(i < ready),
            });
        }

        row.AddChild(new Label { Text = $"{ready}/{count}", Margin = new Thickness(3, 0, 0, 0) });
        button.AddChild(row);
        button.OnPressed += _ => RankPressed?.Invoke(rank);
        return button;
    }

    private static string Key(CFSpellbookComponent? book)
    {
        if (book == null)
            return string.Empty;

        return book.Prepared + "|" + string.Join(',', book.Slots) + "|" + string.Join(',', book.Expended) + "|" + book.FocusPoints + "|" + book.FocusSpent;
    }

    private static StyleBoxFlat Box(bool ready)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = ready ? new Color(64, 140, 210) : new Color(48, 48, 54),
            BorderColor = ready ? new Color(186, 220, 245) : new Color(90, 90, 98),
            BorderThickness = new Thickness(1),
        };
    }
}
