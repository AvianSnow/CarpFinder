using Content.Shared.CF.Spells;

namespace Content.Shared.CF.Character.Grants;

public enum CFPsychicOption : byte
{
    /// <summary>The Oscillating Wave's Conservation of Energy: its spells deal fire and cold in turn.</summary>
    Oscillation,

    /// <summary>Psychic Rapport: +1 circumstance to Deception, Diplomacy and Intimidation after a mental spell.</summary>
    Rapport,
}

/// <summary>A flag on <see cref="CFPsychicComponent"/>. <see cref="CFPsiSystem"/> reads them as spells are cast.</summary>
public sealed partial class CFGrantPsychicOption : CFGrant
{
    [DataField(required: true)]
    public CFPsychicOption Option;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var psychic = entities.EnsureComponent<CFPsychicComponent>(args.Mob);
        switch (Option)
        {
            case CFPsychicOption.Oscillation:
                psychic.Oscillation = true;
                break;
            case CFPsychicOption.Rapport:
                psychic.Rapport = true;
                break;
        }
    }
}
