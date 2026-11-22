using Content.Shared.CF.Languages;
using Content.Shared.Speech.Components;
using Content.Shared.Speech.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Languages;

/// <summary>
/// A creature with languages speaks real words. Stock bodies such as the kobold carry an accent that swaps the whole
/// line for an animal noise before any language is looked at, so nobody understood it, not even the speaker. A language
/// already decides who hears nonsense, so that accent goes. Accents that only change words or letters stay.
/// </summary>
public sealed partial class CFLanguageAccentSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFLanguagesComponent> ent, ref MapInitEvent args)
    {
        if (TryComp(ent, out ReplacementAccentComponent? accent)
            && _prototypes.TryIndex<ReplacementAccentPrototype>(accent.Accent, out var prototype)
            && prototype.FullReplacements != null)
        {
            RemComp<ReplacementAccentComponent>(ent);
        }
    }
}
