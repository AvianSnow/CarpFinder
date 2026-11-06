using System.Diagnostics.CodeAnalysis;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Languages;

/// <summary>
/// Which language a creature speaks, and whether another creature understands it. Choosing one is a verb on yourself,
/// because language needs no new key. <c>ChatSystem</c> asks this system for each listener.
/// </summary>
public sealed partial class CFLanguageSystem : EntitySystem
{
    private static readonly VerbCategory SpeakCategory = new("cf-verb-speak-language", null);

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>A creature with no languages at all understands everything: ghosts, machines, stock mobs.</summary>
    public bool Understands(EntityUid? listener, ProtoId<CFLanguagePrototype> language)
    {
        return !TryComp(listener, out CFLanguagesComponent? languages) || languages.Known.Contains(language);
    }

    /// <summary>What this creature speaks now. False if it has no languages, so its speech is left alone.</summary>
    public bool TryGetSpoken(EntityUid speaker, [NotNullWhen(true)] out CFLanguagePrototype? language)
    {
        language = null;
        if (!TryComp(speaker, out CFLanguagesComponent? languages) || languages.Known.Count == 0)
            return false;

        return _prototypes.TryIndex(Spoken(languages), out language);
    }

    private static ProtoId<CFLanguagePrototype> Spoken(CFLanguagesComponent languages)
    {
        if (languages.Speaking is { } speaking && languages.Known.Contains(speaking))
            return speaking;

        return languages.Known.Contains(CFLanguageRules.DefaultLanguage)
            ? CFLanguageRules.DefaultLanguage
            : languages.Known[0];
    }

    /// <summary>Switches language. False if it does not know that one.</summary>
    public bool TrySpeak(Entity<CFLanguagesComponent> ent, ProtoId<CFLanguagePrototype> language)
    {
        if (!ent.Comp.Known.Contains(language))
            return false;

        ent.Comp.Speaking = language;
        Dirty(ent);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFLanguagesComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User != ent.Owner || ent.Comp.Known.Count == 0)
            return;

        var speaking = Spoken(ent.Comp);
        foreach (var id in ent.Comp.Known)
        {
            if (!_prototypes.TryIndex(id, out var language))
                continue;

            var name = Loc.GetString(language.Name);
            args.Verbs.Add(new Verb
            {
                Category = SpeakCategory,
                Text = name,
                Disabled = id == speaking,
                Act = () =>
                {
                    if (TrySpeak(ent, id))
                        _popup.PopupEntity(Loc.GetString("cf-language-now-speaking", ("language", name)), ent, ent);
                },
            });
        }
    }
}
