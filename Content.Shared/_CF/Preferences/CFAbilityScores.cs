using Robust.Shared.Serialization;

namespace Content.Shared.CF.Preferences;

/// <summary>
/// Helpers for Carpfinder attribute modifiers stored on profiles / components.
/// </summary>
public static class CFAbilityScores
{
    public static string FormatModifier(int modifier)
    {
        return modifier >= 0 ? $"+{modifier}" : modifier.ToString();
    }
}
