using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Languages;

/// <summary>
/// Which language a creature speaks, and whether another creature understands it. A player chooses one in the language
/// menu, opened from the top bar. <c>ChatSystem</c> asks this system for each listener.
/// </summary>
public sealed partial class CFLanguageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

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

    /// <summary>The language it speaks now: the one chosen, else Common, else the first it knows. Needs one known.</summary>
    public static ProtoId<CFLanguagePrototype> Spoken(CFLanguagesComponent languages)
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

    /// <summary>The language menu's choice. It comes from a client, so <see cref="TrySpeak"/> checks it.</summary>
    [EventSubscription]
    private void OnSpeakRequest(CFSpeakLanguageEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } speaker && TryComp(speaker, out CFLanguagesComponent? languages))
            TrySpeak((speaker, languages), msg.Language);
    }
}

/// <summary>
/// Sent by the language menu: speak this language from now on.
/// After <c>LanguagesSetMessage</c> in Starlight (https://github.com/ss14Starlight/space-station-14).
/// </summary>
[Serializable, NetSerializable]
public sealed class CFSpeakLanguageEvent : EntityEventArgs
{
    public string Language = string.Empty;
}
