using Content.Shared.CF.Languages;
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

    /// <summary>Subclass picks, one per slot. Read them through <see cref="CFClassChoices"/>, which checks they are valid.</summary>
    [DataField]
    public List<ProtoId<CFClassChoicePrototype>> ClassChoices = new();

    /// <summary>The deity of a class that follows one. Read it through <see cref="CFClassChoices"/>.</summary>
    [DataField]
    public ProtoId<CFDeityPrototype>? Deity;

    /// <summary>Cantrips and skills chosen for feats, heritages and subclasses. Read them through <see cref="CFOptionChoices"/>.</summary>
    [DataField]
    public List<CFOptionPick> OptionPicks = new();

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

    /// <summary>Level 2 skill feat.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? SkillFeatLevel2;

    /// <summary>Level 3 general feat. A skill feat also fits here.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? GeneralFeatLevel3;

    /// <summary>Level 4 skill feat.</summary>
    [DataField]
    public ProtoId<CFFeatPrototype>? SkillFeatLevel4;

    /// <summary>The skill raised one rank at level 3.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? SkillIncreaseLevel3;

    /// <summary>The skill raised one rank at level 5.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? SkillIncreaseLevel5;

    /// <summary>The four different attributes boosted at level 5.</summary>
    [DataField]
    public List<CFAbility> AbilityBoostsLevel5 = new();

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

    /// <summary>Extra languages from Intelligence, on top of the ancestry's.</summary>
    [DataField]
    public List<ProtoId<CFLanguagePrototype>> Languages = new();

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

    /// <summary>
    /// Copies bought past the first, per job and loadout: read it through <c>CFCoins.CopiesOf</c>. The first copy is the
    /// stock loadout, which is equipped at spawn. These go in the character's hands and bag. It comes from a client, so
    /// spawning checks it against the purse.
    /// </summary>
    [DataField]
    public Dictionary<string, int> LoadoutCopies = new();

    /// <summary>Skill feats from levels where the class gives an extra one, by level. See <see cref="CFClassPrototype.BonusSkillFeats"/>.</summary>
    [DataField]
    public Dictionary<int, string> BonusSkillFeats = new();

    /// <summary>The same for skill increases. See <see cref="CFClassPrototype.BonusSkillIncreases"/>.</summary>
    [DataField]
    public Dictionary<int, string> BonusSkillIncreases = new();

    public CFCharacterBuildData Clone()
    {
        return new CFCharacterBuildData
        {
            Background = Background,
            Class = Class,
            Heritage = Heritage,
            ClassChoices = ClassChoices.ToList(),
            Deity = Deity,
            OptionPicks = OptionPicks.Select(pick => pick.Copy()).ToList(),
            AncestryFeat = AncestryFeat,
            ClassFeat = ClassFeat,
            ClassFeatLevel2 = ClassFeatLevel2,
            ClassFeatLevel4 = ClassFeatLevel4,
            AncestryFeatLevel5 = AncestryFeatLevel5,
            SkillFeatLevel2 = SkillFeatLevel2,
            GeneralFeatLevel3 = GeneralFeatLevel3,
            SkillFeatLevel4 = SkillFeatLevel4,
            SkillIncreaseLevel3 = SkillIncreaseLevel3,
            SkillIncreaseLevel5 = SkillIncreaseLevel5,
            AbilityBoostsLevel5 = AbilityBoostsLevel5.ToList(),
            AncestryFreeBoosts = AncestryFreeBoosts.ToList(),
            BackgroundBoost = BackgroundBoost,
            BackgroundFreeBoost = BackgroundFreeBoost,
            FreeBoosts = FreeBoosts.ToList(),
            BackgroundTrainedSkills = BackgroundTrainedSkills.ToList(),
            ClassTrainedSkills = ClassTrainedSkills.ToList(),
            Languages = Languages.ToList(),
            Level = Level,
            Spells = Spells.Select(spell => spell.Copy()).ToList(),
            PetId = PetId,
            PetName = PetName,
            PetBarding = PetBarding,
            PetGear = PetGear.ToList(),
            LoadoutCopies = new Dictionary<string, int>(LoadoutCopies),
            BonusSkillFeats = new Dictionary<int, string>(BonusSkillFeats),
            BonusSkillIncreases = new Dictionary<int, string>(BonusSkillIncreases),
        };
    }

    public bool Equals(CFCharacterBuildData? other)
    {
        if (other is null)
            return false;

        return Background == other.Background
               && Class == other.Class
               && Heritage == other.Heritage
               && ClassChoices.SequenceEqual(other.ClassChoices)
               && Deity == other.Deity
               && OptionPicks.SequenceEqual(other.OptionPicks)
               && AncestryFeat == other.AncestryFeat
               && ClassFeat == other.ClassFeat
               && ClassFeatLevel2 == other.ClassFeatLevel2
               && ClassFeatLevel4 == other.ClassFeatLevel4
               && AncestryFeatLevel5 == other.AncestryFeatLevel5
               && SkillFeatLevel2 == other.SkillFeatLevel2
               && GeneralFeatLevel3 == other.GeneralFeatLevel3
               && SkillFeatLevel4 == other.SkillFeatLevel4
               && SkillIncreaseLevel3 == other.SkillIncreaseLevel3
               && SkillIncreaseLevel5 == other.SkillIncreaseLevel5
               && AbilityBoostsLevel5.SequenceEqual(other.AbilityBoostsLevel5)
               && Level == other.Level
               && BackgroundBoost == other.BackgroundBoost
               && BackgroundFreeBoost == other.BackgroundFreeBoost
               && AncestryFreeBoosts.SequenceEqual(other.AncestryFreeBoosts)
               && FreeBoosts.SequenceEqual(other.FreeBoosts)
               && BackgroundTrainedSkills.SequenceEqual(other.BackgroundTrainedSkills)
               && ClassTrainedSkills.SequenceEqual(other.ClassTrainedSkills)
               && Languages.SequenceEqual(other.Languages)
               && Spells.SequenceEqual(other.Spells)
               && PetId == other.PetId
               && PetName == other.PetName
               && PetBarding == other.PetBarding
               && PetGear.SequenceEqual(other.PetGear)
               && LoadoutCopies.Count == other.LoadoutCopies.Count
               && LoadoutCopies.All(pair => other.LoadoutCopies.GetValueOrDefault(pair.Key) == pair.Value)
               && BonusSkillFeats.Count == other.BonusSkillFeats.Count
               && BonusSkillFeats.All(pair => other.BonusSkillFeats.GetValueOrDefault(pair.Key) == pair.Value)
               && BonusSkillIncreases.Count == other.BonusSkillIncreases.Count
               && BonusSkillIncreases.All(pair => other.BonusSkillIncreases.GetValueOrDefault(pair.Key) == pair.Value);
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
        foreach (var choice in ClassChoices)
            hash.Add(choice);
        hash.Add(Deity);
        foreach (var pick in OptionPicks)
            hash.Add(pick);
        hash.Add(AncestryFeat);
        hash.Add(ClassFeat);
        hash.Add(ClassFeatLevel2);
        hash.Add(ClassFeatLevel4);
        hash.Add(Languages.Count);
        hash.Add(SkillFeatLevel2);
        hash.Add(GeneralFeatLevel3);
        hash.Add(SkillFeatLevel4);
        hash.Add(SkillIncreaseLevel3);
        hash.Add(SkillIncreaseLevel5);
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
        hash.Add(LoadoutCopies.Count);
        hash.Add(BonusSkillFeats.Count);
        hash.Add(BonusSkillIncreases.Count);
        hash.Add(PetId);
        hash.Add(PetName);
        hash.Add(PetBarding);
        foreach (var gear in PetGear)
            hash.Add(gear);
        return hash.ToHashCode();
    }
}

/// <summary>One answer to a <see cref="CFOptionChoice"/>.</summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class CFOptionPick : IEquatable<CFOptionPick>
{
    /// <summary>The id of the feat, heritage or subclass option the choice belongs to.</summary>
    [DataField]
    public string Source = string.Empty;

    [DataField]
    public int Slot;

    /// <summary>A spell id or a skill id.</summary>
    [DataField]
    public string Value = string.Empty;

    public CFOptionPick Copy()
    {
        return new CFOptionPick { Source = Source, Slot = Slot, Value = Value };
    }

    public bool Equals(CFOptionPick? other)
    {
        return other != null && Source == other.Source && Slot == other.Slot && Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is CFOptionPick other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Source, Slot, Value);
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
