using Content.Shared.CF.Languages;
using Robust.Shared.Prototypes;

namespace Content.Server.Chat.Systems;

/// <summary>Speech in a language. The CarpFinder marked lines in the rest of this class use it.</summary>
public sealed partial class ChatSystem
{
    [Dependency] private CFLanguageSystem _cfLanguages = default!;

    /// <summary>A line of speech, as it sounds to those with and without the language.</summary>
    private sealed class CFSpeech
    {
        public required ProtoId<CFLanguagePrototype> Language { get; init; }

        /// <summary>The nonsense heard without the language.</summary>
        public required string Garbled { get; init; }

        /// <summary>The real line, saying which language it is in.</summary>
        public required string Tagged { get; init; }

        /// <summary><see cref="Garbled"/> wrapped for the chat window.</summary>
        public string WrappedGarbled = string.Empty;
    }

    /// <summary>Null when the speaker has no languages, so stock mobs and machines speak as they always did.</summary>
    private CFSpeech? CFGetSpeech(EntityUid source, string message)
    {
        if (!_cfLanguages.TryGetSpoken(source, out var language))
            return null;

        var tagged = language.ID == CFLanguageRules.DefaultLanguage
            ? message
            : Loc.GetString("cf-language-tag", ("language", Loc.GetString(language.Name)), ("message", message));

        return new CFSpeech
        {
            Language = language.ID,
            Garbled = CFLanguageRules.Garble(language.Syllables, message),
            Tagged = tagged,
        };
    }

    private bool CFIsForeign(CFSpeech? speech, EntityUid? listener)
    {
        return speech != null && !_cfLanguages.Understands(listener, speech.Language);
    }
}
