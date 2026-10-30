using System.Linq;
using Content.Shared.CF.Character;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Preferences;

/// <summary>
/// Pathfinder 2e build choices stored on <see cref="Content.Shared.Preferences.HumanoidCharacterProfile"/>.
/// Ancestry is implied by <c>Species</c>; this holds background, class, and free/choice boost assignments.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class CFCharacterBuildData : IEquatable<CFCharacterBuildData>
{
    [DataField]
    public ProtoId<CFBackgroundPrototype>? Background;

    [DataField]
    public ProtoId<CFClassPrototype>? Class;

    /// <summary>
    /// Free attribute boosts granted by ancestry (one entry per Free in the ancestry prototype).
    /// </summary>
    [DataField]
    public List<CFAbility> AncestryFreeBoosts = new();

    /// <summary>
    /// Background restricted boost: pick one from the background's non-Free abilityBoosts list.
    /// </summary>
    [DataField]
    public CFAbility? BackgroundBoost;

    /// <summary>
    /// Background free boost (any attribute).
    /// </summary>
    [DataField]
    public CFAbility? BackgroundFreeBoost;

    /// <summary>
    /// The four finishing free boosts (Step 6). Must be four distinct attributes when complete.
    /// </summary>
    [DataField]
    public List<CFAbility> FreeBoosts = new();

    [DataField]
    public int Level = 1;

    public CFCharacterBuildData Clone()
    {
        return new CFCharacterBuildData
        {
            Background = Background,
            Class = Class,
            AncestryFreeBoosts = AncestryFreeBoosts.ToList(),
            BackgroundBoost = BackgroundBoost,
            BackgroundFreeBoost = BackgroundFreeBoost,
            FreeBoosts = FreeBoosts.ToList(),
            Level = Level,
        };
    }

    public bool Equals(CFCharacterBuildData? other)
    {
        if (other is null)
            return false;

        return Background == other.Background
               && Class == other.Class
               && Level == other.Level
               && BackgroundBoost == other.BackgroundBoost
               && BackgroundFreeBoost == other.BackgroundFreeBoost
               && AncestryFreeBoosts.SequenceEqual(other.AncestryFreeBoosts)
               && FreeBoosts.SequenceEqual(other.FreeBoosts);
    }

    public override bool Equals(object? obj)
    {
        return obj is CFCharacterBuildData other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Background);
        hash.Add(Class);
        hash.Add(Level);
        hash.Add(BackgroundBoost);
        hash.Add(BackgroundFreeBoost);
        foreach (var boost in AncestryFreeBoosts)
            hash.Add(boost);
        foreach (var boost in FreeBoosts)
            hash.Add(boost);
        return hash.ToHashCode();
    }
}
