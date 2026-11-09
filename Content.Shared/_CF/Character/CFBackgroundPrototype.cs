using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

[Prototype("cfBackground")]
public sealed partial class CFBackgroundPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> SkillBoosts = new();

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
