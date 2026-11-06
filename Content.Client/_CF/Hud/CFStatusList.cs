using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CF.Hud;

/// <summary>One word or two about what is happening to the player. Good ones are green and bad ones red.</summary>
public readonly record struct CFHudStatus(string Text, bool Good);

/// <summary>Colors the HUD and character sheet share for a bonus and a penalty.</summary>
public static class CFHudColors
{
    public static readonly Color Good = new(96, 220, 96);
    public static readonly Color Bad = new(232, 84, 76);

    /// <summary>Null keeps the control's normal color.</summary>
    public static Color? ForBonus(int bonus)
    {
        return bonus > 0 ? Good : bonus < 0 ? Bad : null;
    }
}

/// <summary>
/// The row of conditions above the action panel. It keeps its height while empty, because the screen moves open bags
/// whenever the HUD changes height.
/// </summary>
public sealed class CFStatusList : PanelContainer
{
    private const int Columns = 6;

    private static readonly StyleBoxFlat Backdrop = new()
    {
        BackgroundColor = new Color(16, 16, 20, 170),
        ContentMarginLeftOverride = 6,
        ContentMarginRightOverride = 6,
    };

    private static readonly StyleBoxEmpty Nothing = new();

    private readonly GridContainer _grid = new()
    {
        Columns = Columns,
        HSeparationOverride = 12,
        VSeparationOverride = 0,
    };

    private string _shown = "";

    public CFStatusList()
    {
        MinHeight = 22f;
        PanelOverride = Nothing;
        AddChild(_grid);
    }

    public void Set(List<CFHudStatus> statuses)
    {
        var key = string.Join('|', statuses.Select(status => $"{status.Text}:{status.Good}"));
        if (key == _shown)
            return;

        _shown = key;
        _grid.RemoveAllChildren();
        foreach (var status in statuses)
        {
            _grid.AddChild(new Label
            {
                Text = status.Text,
                FontColorOverride = status.Good ? CFHudColors.Good : CFHudColors.Bad,
            });
        }

        PanelOverride = statuses.Count > 0 ? Backdrop : Nothing;
    }
}
