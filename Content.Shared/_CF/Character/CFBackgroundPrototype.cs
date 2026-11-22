using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

[Prototype("cfBackground")]
public sealed partial class CFBackgroundPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = default!;

    [DataField("description")]
    public string Description { get; private set; } = default!;

    [DataField("skillRanks")]
    public Dictionary<string, CFSkillRank> SkillRanks { get; private set; } = new();

    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> TrainedSkills = new();

    [DataField]
    public List<CFAbilityBoost> AbilityBoosts = new();

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    /// <summary>What this gives a character. See <see cref="CFGrant"/>.</summary>
    [DataField]
    public List<CFGrant> Grants = new();

    /// <summary>Feats a character has from level 1 for taking this background. They need no qualifying.</summary>
    [DataField]
    public List<ProtoId<CFFeatPrototype>> SkillFeats = new();
}
