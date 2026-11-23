using Content.Shared.CF.Combat;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character.Grants;

/// <summary>Runic Tattoo: a rune etched on the Runesmith for good, outside the etched limit. Once invoked it waits for a long rest.</summary>
public sealed partial class CFGrantRunicTattoo : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFRunePrototype> Rune;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFRuneSystem>().SetTattoo(args.Mob, Rune);
    }
}

/// <summary>A Runesmith feat that changes how its runes are traced or what its Strikes do. See <see cref="CFRunesmithOption"/>.</summary>
public sealed partial class CFGrantRunesmithOption : CFGrant
{
    [DataField(required: true)]
    public CFRunesmithOption Option;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var runes = entities.System<CFRuneSystem>();
        switch (Option)
        {
            case CFRunesmithOption.RuneSinger:
                runes.SetRuneSinger(args.Mob);
                break;
            case CFRunesmithOption.SmithingWeapons:
                runes.SetSmithingWeapons(args.Mob);
                break;
        }
    }
}

public enum CFRunesmithOption : byte
{
    RuneSinger,
    SmithingWeapons,
}
