using Content.Client.UserInterface.Controls;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Character;
using Content.Shared.CF.Spells;
using Robust.Shared.Maths;

namespace Content.Client.CF.Effects;

/// <summary>A spell that can be cast at more than one rank right now asks which, before anything else about the cast.</summary>
public sealed partial class CFRankMenuSystem : EntitySystem
{
    private SimpleRadialMenu? _menu;

    /// <summary>Opens the choice of rank. Returns false when there is only one rank, so the cast should carry on.</summary>
    public bool TryOffer(Entity<ActionComponent> action, Action next)
    {
        if (!TryComp(action.Owner, out CFSpellComponent? spell))
            return false;

        spell.ChosenRank = 0;
        if (spell.Focus
            || spell.Rank <= 0
            || action.Comp.AttachedEntity is not { } user
            || !TryComp(user, out CFSpellbookComponent? book)
            || CFApparitions.UsesSlot(book, spell.Spell, spell.Rank))
        {
            return false;
        }

        var ranks = CFPreparedSpells.CastableRanks(book, spell.Spell, spell.Rank);
        if (ranks.Count < 2)
            return false;

        var options = new List<RadialMenuOptionBase>(ranks.Count);
        foreach (var rank in ranks)
        {
            options.Add(new RadialMenuActionOption<int>(picked =>
            {
                spell.ChosenRank = picked;
                next();
            }, rank)
            {
                Text = rank.ToString(),
                ToolTip = Loc.GetString("cf-spell-rank", ("rank", rank)),
                Order = rank,
                BackgroundColor = new Color(36, 72, 140, 190),
                HoverBackgroundColor = new Color(90, 150, 210, 220),
            });
        }

        _menu?.Close();
        _menu = new SimpleRadialMenu();
        _menu.SetButtons(options);
        _menu.OpenOverMouseScreenPosition();
        return true;
    }
}
