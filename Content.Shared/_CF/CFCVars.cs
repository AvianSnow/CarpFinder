using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.CF;

[CVarDefs]
public sealed class CFCVars : CVars
{
    /// <summary>Keeps the HUD deck open instead of closing it 5 seconds after the mouse leaves.</summary>
    public static readonly CVarDef<bool> HudDeckPinned =
        CVarDef.Create("cf.hud_deck_pinned", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Stops actions being dragged out of or rearranged on the HUD deck.</summary>
    public static readonly CVarDef<bool> HudDeckLocked =
        CVarDef.Create("cf.hud_deck_locked", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>How many rows of icons the HUD deck shows.</summary>
    public static readonly CVarDef<int> HudDeckRows =
        CVarDef.Create("cf.hud_deck_rows", 2, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>How many columns each section of the HUD deck's All tab has, comma-separated.</summary>
    public static readonly CVarDef<string> HudDeckColumns =
        CVarDef.Create("cf.hud_deck_columns", "3,4,1,2", CVar.CLIENTONLY | CVar.ARCHIVE);
}
