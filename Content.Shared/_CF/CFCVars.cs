using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.CF;

[CVarDefs]
public sealed partial class CFCVars : CVars
{
    /// <summary>A character needs an ancestry, background, class and job before it can ready up or join.</summary>
    public static readonly CVarDef<bool> RequireCompleteCharacter =
        CVarDef.Create("cf.require_complete_character", true, CVar.SERVERONLY);

    /// <summary>
    /// GOAP brains sleep while no living player is within their <c>WakeFeet</c>, so mobs far from everyone cost nothing.
    /// Integration tests turn it off, because their heroes have no player in them.
    /// </summary>
    public static readonly CVarDef<bool> GoapSleep =
        CVarDef.Create("cf.goap_sleep", true, CVar.SERVERONLY);

    /// <summary>
    /// A tribe's leader asks a local language model what the tribe should do. Off, or with the model out of reach, the
    /// built-in rules decide, so a tribe works either way. See <c>.agent/society.md</c>.
    /// </summary>
    public static readonly CVarDef<bool> SocietyLlm =
        CVarDef.Create("cf.society_llm", false, CVar.SERVERONLY);

    /// <summary>The chat completion endpoint. The default is llama.cpp's <c>llama-server</c> on this machine.</summary>
    public static readonly CVarDef<string> SocietyLlmUrl =
        CVarDef.Create("cf.society_llm_url", "http://127.0.0.1:8080/v1/chat/completions", CVar.SERVERONLY);

    /// <summary>Seconds before an answer is given up on. A 3B model on a CPU takes a few seconds for 60 tokens.</summary>
    public static readonly CVarDef<float> SocietyLlmTimeout =
        CVarDef.Create("cf.society_llm_timeout", 20f, CVar.SERVERONLY);

    /// <summary>The shortest wait between two questions from one creature, in seconds.</summary>
    public static readonly CVarDef<float> SocietyLlmCooldown =
        CVarDef.Create("cf.society_llm_cooldown", 15f, CVar.SERVERONLY);

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
