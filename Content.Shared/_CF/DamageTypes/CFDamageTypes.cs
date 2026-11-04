using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.DamageTypes;

/// <summary>
/// Looks up <see cref="CFDamageTypePrototype"/> from the free-text damage type strings that weapons,
/// spells, and pets still use (<c>damageType: slashing</c>). Matching ignores case and also checks aliases.
/// </summary>
/// <remarks>
/// Those string fields become <c>ProtoId&lt;CFDamageTypePrototype&gt;</c> when the effect pipeline (spells)
/// and the weapon rework replace their components. Until then, resolve through this helper.
/// </remarks>
public static class CFDamageTypes
{
    public const string Bludgeoning = "Bludgeoning";

    public static bool TryResolve(
        IPrototypeManager prototypes,
        string? name,
        [NotNullWhen(true)] out CFDamageTypePrototype? damageType)
    {
        damageType = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (prototypes.TryIndex(name, out damageType))
            return true;

        foreach (var proto in prototypes.EnumeratePrototypes<CFDamageTypePrototype>())
        {
            if (string.Equals(proto.ID, name, StringComparison.OrdinalIgnoreCase))
            {
                damageType = proto;
                return true;
            }

            foreach (var alias in proto.Aliases)
            {
                if (!string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                damageType = proto;
                return true;
            }
        }

        return false;
    }

    /// <summary>True when both strings name the same damage type, counting aliases and case.</summary>
    public static bool Same(IPrototypeManager prototypes, string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        return TryResolve(prototypes, a, out var first)
               && TryResolve(prototypes, b, out var second)
               && first.ID == second.ID;
    }
}
