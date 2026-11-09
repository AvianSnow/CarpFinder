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

    /// <summary>Finesse melee Strikes may add Dexterity to damage instead of Strength (the Thief racket).</summary>
    [DataField, AutoNetworkedField]
    public bool FinesseDexterity;

    /// <summary>Sneak Attack works with any weapon within the die limits (Ruffian).</summary>
    [DataField, AutoNetworkedField]
    public bool AnyWeaponSneak;

    /// <summary>The first hit on your prey each turn deals 1d8 precision (Precision edge).</summary>
    [DataField, AutoNetworkedField]
    public bool PreyPrecision;

    /// <summary>Multiple attack penalty is smaller against your prey (Flurry edge).</summary>
    [DataField, AutoNetworkedField]
    public bool PreyFlurry;

    /// <summary>+1 circumstance AC against your prey (Outwit edge).</summary>
    [DataField, AutoNetworkedField]
    public bool PreyOutwit;

    /// <summary>Frightened is 1 lower whenever you gain it.</summary>
    [DataField, AutoNetworkedField]
    public bool Bravery;

    /// <summary>Not off-guard to flanking creatures of your level or lower.</summary>
    [DataField, AutoNetworkedField]
    public bool DenyAdvantage;

    /// <summary>Frightened creatures are off-guard to you.</summary>
    [DataField, AutoNetworkedField]
    public bool DreadStriker;

    /// <summary>A critical hit that deals damage frightens the target.</summary>
    [DataField, AutoNetworkedField]
    public bool BrutalBeating;

    /// <summary>A critical hit that deals damage leaves the target off-guard to you.</summary>
    [DataField, AutoNetworkedField]
    public bool UnbalancingBlow;

    /// <summary>Range increments are doubled.</summary>
    [DataField, AutoNetworkedField]
    public bool FarShot;

    /// <summary>Thrown range increments are 10 feet longer.</summary>
    [DataField, AutoNetworkedField]
    public bool StrongArm;

    /// <summary>Walking out of reach needs no action (Mobility).</summary>
    [DataField, AutoNetworkedField]
    public bool FreeStep;

    /// <summary>Walking out of reach needs no action while your shield is raised (Shielded Stride).</summary>
    [DataField, AutoNetworkedField]
    public bool FreeStepWithShield;

    /// <summary>The heal spell rolls d10s instead of d8s.</summary>
    [DataField, AutoNetworkedField]
    public bool HealingHands;

    /// <summary>The harm spell rolls d10s instead of d8s.</summary>
    [DataField, AutoNetworkedField]
    public bool HarmingHands;

    /// <summary>Frightened eases by 2 each turn instead of 1 (Dwarven Doughtiness).</summary>
    [DataField, AutoNetworkedField]
    public bool ShakesOffFear;

    /// <summary>Worn armor does not reduce Speed (Unburdened Iron).</summary>
    [DataField, AutoNetworkedField]
    public bool IgnoresArmorSpeed;

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
    public bool ReactiveShield;

    [DataField, AutoNetworkedField]
    public bool YoureNext;

    /// <summary>A healer's toolkit used in hand in combat mode heals for one action.</summary>
    [DataField, AutoNetworkedField]
    public bool BattleMedicine;

    /// <summary>A successful Tumble Through leaves the enemy off-guard to the tumbler.</summary>
    [DataField, AutoNetworkedField]
    public bool TumbleBehind;

    /// <summary>Playing a held instrument performs Courageous Anthem.</summary>
    [DataField, AutoNetworkedField]
    public bool Anthem;

    /// <summary>Server-only. When a Bard still playing pays for the next anthem.</summary>
    public TimeSpan NextAnthem;

    /// <summary>A hit with a hand free leaves the target off-guard.</summary>
    [DataField, AutoNetworkedField]
    public bool SnaggingStrike;

    /// <summary>A plain miss with a multiple attack penalty does not add to it.</summary>
    [DataField, AutoNetworkedField]
    public bool ExactingStrike;

    /// <summary>A wide swing at one creature with a weapon in each hand Strikes with both.</summary>
    [DataField, AutoNetworkedField]
    public bool DoubleSlice;

    /// <summary>A wide swing at one creature with a weapon in each hand Strikes with both, and the second finds it off-guard.</summary>
    [DataField, AutoNetworkedField]
    public bool TwinFeint;

    /// <summary>A wide swing at one creature adds a weapon die.</summary>
    [DataField, AutoNetworkedField]
    public bool ViciousSwing;

    /// <summary>What pointing at an enemy does: a Ranger's prey, a Thaumaturge's or Investigator's mark.</summary>
    [DataField, AutoNetworkedField]
    public CFPointMark PointMark;

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
    /// <summary>Any reaction from <see cref="CFReactionSystem"/>.</summary>
    public const int Granted = 4;
    public const int Raise = 8;
    public const int Champion = 16;
    public const int Next = 32;

    public static bool Knows(CFCombatOptionsComponent opt, int bit)
    {
        return bit switch
        {
            Strike => opt.ReactiveStrike,
            Block => opt.ShieldBlock,
            Raise => opt.ReactiveShield,
            Champion => opt.Champion,
            Next => opt.YoureNext,
            _ => false,
        };
    }
}

/// <summary>The flags on <see cref="CFCombatOptionsComponent"/> that a <c>CFGrantOption</c> can set.</summary>
public enum CFCombatOption : byte
{
    SneakAttack,
    CanRage,
    ReactiveStrike,
    ShieldBlock,
    Flurry,
    PowerfulFist,
    PanacheStyle,
    Spellstrike,
    Devise,
    Exploit,
    Champion,
    Anthem,
    BattleMedicine,
    ReactiveShield,
    YoureNext,
    TumbleBehind,
    SnaggingStrike,
    ExactingStrike,
    DoubleSlice,
    TwinFeint,
    ViciousSwing,
    FinesseDexterity,
    ShakesOffFear,
    IgnoresArmorSpeed,
    HealingHands,
    HarmingHands,
    AnyWeaponSneak,
    PreyPrecision,
    PreyFlurry,
    PreyOutwit,
    Bravery,
    DenyAdvantage,
    DreadStriker,
    BrutalBeating,
    UnbalancingBlow,
    FarShot,
    StrongArm,
    FreeStep,
    FreeStepWithShield,
}

public enum CFPointMark : byte
{
    None,
    Prey,
    Exploit,
    Stratagem,
}
