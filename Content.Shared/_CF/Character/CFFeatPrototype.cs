using Content.Shared.CF.Character.Grants;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

public enum CFFeatAction
{
    Passive,
    Action,
    Reaction,
    Free,
}

/// <summary>
/// A Carpfinder feat. Ancestry feats through level 5 live under Resources/Prototypes/_CF/Character/Feats/Ancestry.
/// Descriptions are short mechanical notes. An activity the game cannot perform yet says so in the description.
/// </summary>
[Prototype("cfFeat")]
public sealed partial class CFFeatPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>Set on an ancestry feat.</summary>
    [DataField]
    public ProtoId<CFAncestryPrototype>? Ancestry;

    /// <summary>Set on a class feat. Matches the class id, such as Bard or Fighter.</summary>
    [DataField]
    public string? Class;

    /// <summary>Class feat that more than one class can take. The picker offers it to each listed class.</summary>
    [DataField]
    public List<string> Classes = new();

    [DataField]
    public int Level = 1;

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    [DataField]
    public List<string> Traits = new();

    [DataField]
    public List<string> Prerequisites = new();

    /// <summary>Skills this feat trains. Lore is included when <see cref="Lores"/> is set.</summary>
    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> TrainedSkills = new();

    /// <summary>Specific Lore skills, such as Goblin Lore. The sheet trains Lore and shows these names.</summary>
    [DataField]
    public List<string> Lores = new();

    /// <summary>What the player picks when taking this.</summary>
    [DataField]
    public List<CFOptionChoice> Choices = new();

    /// <summary>Feet added to land Speed. Applied when the feat is chosen.</summary>
    [DataField]
    public int SpeedBonus;

    /// <summary>Replaces the fist Strike when this feat is chosen. 0 keeps the fist.</summary>
    [DataField]
    public int NaturalDice;

    [DataField]
    public int NaturalDie;

    [DataField]
    public string NaturalDamage = "";

    [DataField]
    public bool NaturalFinesse;

    [DataField]
    public bool NaturalAgile = true;

    [DataField]
    public CFFeatAction Action = CFFeatAction.Passive;

    /// <summary>What this gives a character. See <see cref="CFGrant"/>.</summary>
    [DataField]
    public List<CFGrant> Grants = new();

    /// <summary>The feat grants an activity or attack the game cannot perform yet.</summary>
    [DataField]
    public bool PendingAction;

    /// <summary>Set on general and skill feats, which belong to no ancestry or class.</summary>
    [DataField]
    public CFFeatCategory Category = CFFeatCategory.None;

    /// <summary>A skill this feat needs, where the prerequisite is that simple. Anything else stays in the text.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? RequiresSkill;

    [DataField]
    public CFProficiencyRank RequiresRank = CFProficiencyRank.Trained;

    /// <summary>A skill feat slot takes skill feats only.</summary>
    public bool FitsSkillSlot(int level)
    {
        return Category == CFFeatCategory.Skill && Level <= level;
    }

    /// <summary>A general feat slot also takes a skill feat, because every skill feat is a general feat.</summary>
    public bool FitsGeneralSlot(int level)
    {
        return Category != CFFeatCategory.None && Level <= level;
    }

    public bool Fits(string? ancestryId, int level)
    {
        if (Ancestry is not { } ancestry || string.IsNullOrEmpty(ancestry.Id))
            return false;

        return ancestryId != null
               && ancestry.Id == ancestryId
               && Level <= Math.Max(1, level);
    }

    /// <summary>True when this class feat can be taken by <paramref name="classId"/> at the given slot.</summary>
    public bool FitsClass(string? classId, int maxLevel)
    {
        if (string.IsNullOrEmpty(classId) || Level > maxLevel)
            return false;

        if (!string.IsNullOrEmpty(Class))
            return string.Equals(Class, classId, StringComparison.Ordinal);

        foreach (var allowed in Classes)
        {
            if (string.Equals(allowed, classId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public enum CFFeatCategory : byte
{
    None,
    General,
    Skill,
}
