using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Class features and chosen feats that change a Strike. Granted at spawn.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCombatOptionsComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool SneakAttack;

    [DataField, AutoNetworkedField]
    public bool CanRage;

    [DataField, AutoNetworkedField]
    public bool Raging;

    [DataField, AutoNetworkedField]
    public bool ReactiveStrike;

    [DataField, AutoNetworkedField]
    public bool ShieldBlock;

    [DataField, AutoNetworkedField]
    public bool Flurry;

    [DataField, AutoNetworkedField]
    public bool PowerfulFist;

    [DataField, AutoNetworkedField]
    public bool PanacheStyle;

    [DataField, AutoNetworkedField]
    public bool Panache;

    [DataField, AutoNetworkedField]
    public bool Spellstrike;

    [DataField, AutoNetworkedField]
    public bool Devise;

    [DataField, AutoNetworkedField]
    public bool Exploit;

    [DataField, AutoNetworkedField]
    public bool Champion;

    [DataField, AutoNetworkedField]
    public bool NimbleDodge;

    [DataField, AutoNetworkedField]
    public bool ReactiveShield;

    [DataField, AutoNetworkedField]
    public bool YoureNext;

    [DataField, AutoNetworkedField]
    public bool PointBlank;

    /// <summary>Non-zero after this creature has used its reaction. Cleared when the pool refills.</summary>
    [DataField, AutoNetworkedField]
    public int ReactionSpent;

    /// <summary>Server-only. The ranger's hunted creature.</summary>
    public EntityUid? Prey;

    public bool PreyHitThisTurn;

    /// <summary>0 when Devise a Stratagem has not stored a d20.</summary>
    public int StratagemRoll;

    public EntityUid? StratagemTarget;

    public EntityUid? Exploited;

    [DataField, AutoNetworkedField]
    public int NaturalDice;

    [DataField, AutoNetworkedField]
    public int NaturalDie;

    [DataField, AutoNetworkedField]
    public string NaturalType = "";

    [DataField, AutoNetworkedField]
    public bool NaturalFinesse;

    [DataField, AutoNetworkedField]
    public bool NaturalAgile = true;
}

/// <summary>One reaction per full action pool. The value is which reaction was spent.</summary>
public static class CFReactions
{
    public const int Strike = 1;
    public const int Block = 2;
    public const int Nimble = 4;
    public const int Raise = 8;
    public const int Champion = 16;
    public const int Next = 32;

    public static bool Knows(CFCombatOptionsComponent opt, int bit)
    {
        return bit switch
        {
            Strike => opt.ReactiveStrike,
            Block => opt.ShieldBlock,
            Nimble => opt.NimbleDodge,
            Raise => opt.ReactiveShield,
            Champion => opt.Champion,
            Next => opt.YoureNext,
            _ => false,
        };
    }
}
