using System.Linq;
using Content.Shared.CF.Character;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Preferences;

/// <summary>
/// Carpfinder 2e build choices stored on <see cref="Content.Shared.Preferences.HumanoidCharacterProfile"/>.
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

    /// <summary>Chosen heritage. Must match the ancestry, unless it is versatile.</summary>
    [DataField]
    public ProtoId<CFHeritagePrototype>? Heritage;

    /// <summary>Level 1 ancestry feat. Must match the ancestry and be no higher than <see cref="Level"/>.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? AncestryFeat;

    /// <summary>Level 1 class feat.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? ClassFeat;

    /// <summary>Level 2 class feat. It can be a class feat of level 2 or lower.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? ClassFeatLevel2;

    /// <summary>Level 4 class feat. It can be a class feat of level 4 or lower.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? ClassFeatLevel4;

    /// <summary>Level 5 ancestry feat. It can be an ancestry feat of level 5 or lower.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? AncestryFeatLevel5;

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

    /// <summary>
    /// Skills chosen for background skill trainings (one per background skill slot).
    /// </summary>
    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> BackgroundTrainedSkills = new();

    /// <summary>
    /// Free class skill trainings chosen at level 1 (in addition to fixed class skills).
    /// </summary>
    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> ClassTrainedSkills = new();

    [DataField]
    public int Level = 1;

    /// <summary>
    /// Spells chosen for each rank slot. Bonus spells use <see cref="CFSpellSlots.BonusRank"/>.
    /// </summary>
    [DataField]
    public List<CFSpellPick> Spells = new();

    /// <summary>Chosen animal companion or familiar prototype. Empty until the pet tab saves one.</summary>
    [DataField]
    public string PetId = "";

    /// <summary>Nickname shown on the pet tab. Empty uses the species name.</summary>
    [DataField]
    public string PetName = "";

    /// <summary>Empty, light, or heavy. Familiars leave this empty.</summary>
    [DataField]
    public string PetBarding = "";

    /// <summary>Companion gear prototype ids picked in the equipment popup.</summary>
    [DataField]
    public List<string> PetGear = new();

    public CFCharacterBuildData Clone()
    {
        return new CFCharacterBuildData
        {
            Background = Background,
            Class = Class,
            Heritage = Heritage,
            AncestryFeat = AncestryFeat,
            ClassFeat = ClassFeat,
            ClassFeatLevel2 = ClassFeatLevel2,
            ClassFeatLevel4 = ClassFeatLevel4,
            AncestryFeatLevel5 = AncestryFeatLevel5,
            AncestryFreeBoosts = AncestryFreeBoosts.ToList(),
            BackgroundBoost = BackgroundBoost,
            BackgroundFreeBoost = BackgroundFreeBoost,
            FreeBoosts = FreeBoosts.ToList(),
            BackgroundTrainedSkills = BackgroundTrainedSkills.ToList(),
            ClassTrainedSkills = ClassTrainedSkills.ToList(),
            Level = Level,
            Spells = Spells.Select(spell => spell.Copy()).ToList(),
            PetId = PetId,
            PetName = PetName,
            PetBarding = PetBarding,
            PetGear = PetGear.ToList(),
        };
    }

    public bool Equals(CFCharacterBuildData? other)
    {
        if (other is null)
            return false;

        return Background == other.Background
               && Class == other.Class
               && Heritage == other.Heritage
               && AncestryFeat == other.AncestryFeat
               && ClassFeat == other.ClassFeat
               && ClassFeatLevel2 == other.ClassFeatLevel2
               && ClassFeatLevel4 == other.ClassFeatLevel4
               && AncestryFeatLevel5 == other.AncestryFeatLevel5
               && Level == other.Level
               && BackgroundBoost == other.BackgroundBoost
               && BackgroundFreeBoost == other.BackgroundFreeBoost
               && AncestryFreeBoosts.SequenceEqual(other.AncestryFreeBoosts)
               && FreeBoosts.SequenceEqual(other.FreeBoosts)
               && BackgroundTrainedSkills.SequenceEqual(other.BackgroundTrainedSkills)
               && ClassTrainedSkills.SequenceEqual(other.ClassTrainedSkills)
               && Spells.SequenceEqual(other.Spells)
               && PetId == other.PetId
               && PetName == other.PetName
               && PetBarding == other.PetBarding
               && PetGear.SequenceEqual(other.PetGear);
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
        hash.Add(Heritage);
        hash.Add(AncestryFeat);
        hash.Add(ClassFeat);
        hash.Add(ClassFeatLevel2);
        hash.Add(ClassFeatLevel4);
        hash.Add(AncestryFeatLevel5);
        hash.Add(Level);
        hash.Add(BackgroundBoost);
        hash.Add(BackgroundFreeBoost);
        foreach (var boost in AncestryFreeBoosts)
            hash.Add(boost);
        foreach (var boost in FreeBoosts)
            hash.Add(boost);
        foreach (var skill in BackgroundTrainedSkills)
            hash.Add(skill);
        foreach (var skill in ClassTrainedSkills)
            hash.Add(skill);
        foreach (var spell in Spells)
            hash.Add(spell);
        hash.Add(PetId);
        hash.Add(PetName);
        hash.Add(PetBarding);
        foreach (var gear in PetGear)
            hash.Add(gear);
        return hash.ToHashCode();
    }
}

/// <summary>A spell placed in one character-builder slot.</summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class CFSpellPick : IEquatable<CFSpellPick>
{
    [DataField]
    public int Rank;

    [DataField]
    public int Index;

    [DataField]
    public ProtoId<CFSpellPrototype> Spell;

    /// <summary>Unused. Kept so older saved profiles still load.</summary>
    [DataField]
    public bool Expended;

    public bool HasSpell => !string.IsNullOrEmpty(Spell.Id);

    public CFSpellPick Copy()
    {
        return new CFSpellPick
        {
            Rank = Rank,
            Index = Index,
            Spell = Spell,
            Expended = Expended,
        };
    }

    public bool Equals(CFSpellPick? other)
    {
        return other != null
               && Rank == other.Rank
               && Index == other.Index
               && Spell == other.Spell
               && Expended == other.Expended;
    }

    public override bool Equals(object? obj)
    {
        return obj is CFSpellPick other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Rank, Index, Spell, Expended);
    }
}
