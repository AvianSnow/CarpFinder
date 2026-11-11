using Content.Shared.CF.Actions;
using Content.Shared.CF.Spells;

namespace Content.Client.CF.Hud;

public enum CFDeckTab : byte
{
    /// <summary>Every section side by side, with dividers between them. Never an action's own place.</summary>
    All,
    Common,
    ClassFeatures,
    Spells,
    Custom,
}

/// <summary>Where an action goes in the deck.</summary>
/// <param name="Tab">Which tab it is listed on.</param>
/// <param name="SpellGroup">On the Spells tab: 0 for cantrips, a rank from 1, or <see cref="CFDeckSorter.FocusGroup"/>.</param>
public readonly record struct CFDeckPlace(CFDeckTab Tab, int SpellGroup = 0);

/// <summary>Sorts actions into the deck's tabs. Only an action with a radial slot has a place: the rest are Custom's.</summary>
public static class CFDeckSorter
{
    /// <summary>The Spells tab's sub-tab after the ten ranks.</summary>
    public const int FocusGroup = 11;

    /// <returns>Null for what is not listed on a tab, which is a reaction, or anything from stock.</returns>
    public static CFDeckPlace? Place(CFRadialSlotComponent? slot, CFSpellComponent? spell)
    {
        if (spell != null)
            return new CFDeckPlace(CFDeckTab.Spells, spell.Focus ? FocusGroup : Math.Clamp(spell.Rank, 0, 10));

        if (slot == null || slot.Group == CFRadialGroup.Reaction)
            return null;

        return slot.Tab switch
        {
            CFDeckSection.Common => new CFDeckPlace(CFDeckTab.Common),
            CFDeckSection.Focus => new CFDeckPlace(CFDeckTab.Spells, FocusGroup),
            _ => new CFDeckPlace(CFDeckTab.ClassFeatures),
        };
    }
}
