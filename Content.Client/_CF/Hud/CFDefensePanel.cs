using Content.Client.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.CF.Hud;

/// <summary>
/// The three saves and Perception, each with its modifier and rank letter, in the action panel.
/// A modifier raised or lowered by anything right now turns green or red.
/// </summary>
public sealed class CFDefensePanel : GridContainer
{
    /// <summary>In reading order, two to a row.</summary>
    public static readonly CFDefenseType[] Shown =
    {
        CFDefenseType.Fortitude,
        CFDefenseType.Will,
        CFDefenseType.Reflex,
        CFDefenseType.Perception,
    };

    private readonly Label[] _values = new Label[4];
    private readonly Label[] _ranks = new Label[4];

    public CFDefensePanel()
    {
        Columns = 6;
        HSeparationOverride = 5;
        VSeparationOverride = 0;
        Visible = false;

        for (var i = 0; i < Shown.Length; i++)
        {
            AddChild(new Label { Text = Loc.GetString($"cf-hud-defense-{Shown[i].ToString().ToLowerInvariant()}") });
            _values[i] = new Label { MinWidth = 28f, Align = Label.AlignMode.Right };
            AddChild(_values[i]);
            _ranks[i] = new Label { MinWidth = 18f, StyleClasses = { "LabelSubText" } };
            AddChild(_ranks[i]);
        }
    }

    /// <param name="index">Which entry of <see cref="Shown"/>.</param>
    /// <param name="modifier">The modifier a roll would use now.</param>
    /// <param name="plain">The same with no bonuses, penalties or conditions.</param>
    public void Set(int index, int modifier, int plain, CFProficiencyRank rank)
    {
        _values[index].Text = CFSkills.FormatSigned(modifier);
        _values[index].FontColorOverride = CFHudColors.ForBonus(modifier - plain);
        _ranks[index].Text = CFSheetSkillRow.RankLetter(rank);
        _ranks[index].ToolTip = CFSheetSkillRow.RankTooltip(rank);
    }
}
