using System.Linq;
using System.Text;
using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Languages;

/// <summary>
/// Who knows what, and what a language sounds like to everyone else. A character knows their ancestry's languages, and one
/// more for each point of positive Intelligence.
/// </summary>
public static class CFLanguageRules
{
    public const string DefaultLanguage = "Common";

    /// <summary>More than Intelligence can ever give, so a saved profile cannot hold an endless list.</summary>
    public const int MaxPicks = 10;

    /// <summary>Letters of a real word per nonsense syllable.</summary>
    private const int LettersPerSyllable = 3;

    public static int PickCount(IReadOnlyDictionary<CFAbility, int> modifiers)
    {
        return Math.Max(0, modifiers.GetValueOrDefault(CFAbility.Intelligence));
    }

    /// <summary>The languages a character could pick: the common ones their ancestry does not already give.</summary>
    public static List<CFLanguagePrototype> Pickable(IPrototypeManager prototypes, CFAncestryPrototype? ancestry)
    {
        return prototypes.EnumeratePrototypes<CFLanguagePrototype>()
            .Where(language => language.Rarity == CFRarity.Common && ancestry?.Languages.Contains(language.ID) != true)
            .OrderBy(language => language.ID, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// What a character knows. The picks came from a client, so anything it could not have picked is ignored here.
    /// </summary>
    public static List<ProtoId<CFLanguagePrototype>> Known(
        IPrototypeManager prototypes,
        CFAncestryPrototype? ancestry,
        IEnumerable<ProtoId<CFLanguagePrototype>> picks,
        IReadOnlyDictionary<CFAbility, int> modifiers)
    {
        var known = new List<ProtoId<CFLanguagePrototype>>();
        if (ancestry != null)
            known.AddRange(ancestry.Languages);

        if (known.Count == 0)
            known.Add(DefaultLanguage);

        var allowed = Pickable(prototypes, ancestry);
        var left = PickCount(modifiers);
        foreach (var pick in picks)
        {
            if (left <= 0)
                break;

            if (known.Contains(pick) || allowed.All(language => language.ID != pick.Id))
                continue;

            known.Add(pick);
            left--;
        }

        return known;
    }

    /// <summary>
    /// What a line sounds like without the language. A word always turns into the same nonsense, so a listener can start
    /// to recognise words, and punctuation stays so a shouted question still sounds like one.
    /// </summary>
    public static string Garble(IReadOnlyList<string> syllables, string message)
    {
        if (syllables.Count == 0)
            return message;

        var result = new StringBuilder(message.Length);
        var word = new StringBuilder();
        foreach (var letter in message)
        {
            if (char.IsLetterOrDigit(letter))
            {
                word.Append(letter);
                continue;
            }

            AppendWord(result, word, syllables);
            result.Append(letter);
        }

        AppendWord(result, word, syllables);
        return result.ToString();
    }

    private static void AppendWord(StringBuilder result, StringBuilder word, IReadOnlyList<string> syllables)
    {
        if (word.Length == 0)
            return;

        var text = word.ToString();
        word.Clear();

        // Not string.GetHashCode: that changes with every run of the server.
        var hash = 2166136261u;
        foreach (var letter in text.ToLowerInvariant())
        {
            hash = (hash ^ letter) * 16777619u;
        }

        var start = result.Length;
        var count = Math.Max(1, (text.Length + LettersPerSyllable - 1) / LettersPerSyllable);
        for (var i = 0; i < count; i++)
        {
            result.Append(syllables[(int) (hash % (uint) syllables.Count)]);
            hash = hash * 16777619u + 1u;
        }

        if (char.IsUpper(text[0]))
            result[start] = char.ToUpperInvariant(result[start]);
    }
}
