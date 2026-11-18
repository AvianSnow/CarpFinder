using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;

namespace Content.Shared.CF.Actions;

/// <summary>
/// An action that is a Strike with something extra. Parent an action on <c>CFActStrikeBase</c>, say what it needs and what a hit
/// does, and grant it with <c>CFGrantAction</c>. <see cref="CFStrikeActionSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFStrikeActionComponent : Component
{
    /// <summary>Needs a loaded ranged weapon in hand, and spends the arrow. Otherwise it is a melee Strike.</summary>
    [DataField]
    public bool Ranged;

    /// <summary>It has to be an unarmed attack.</summary>
    [DataField]
    public bool UnarmedOnly;

    /// <summary>Needs a melee weapon in each hand, and makes one Strike with each.</summary>
    [DataField]
    public bool TwoWeapons;

    /// <summary>Needs a one-handed melee weapon and a free hand. The weapon hits harder for this Strike.</summary>
    [DataField]
    public bool TwoHandedGrip;

    /// <summary>Only against the creature the user is hunting.</summary>
    [DataField]
    public bool PreyOnly;

    /// <summary>Only against a prone creature.</summary>
    [DataField]
    public bool ProneTarget;

    /// <summary>A finisher: it needs panache, spends it, and adds the finisher's precision damage.</summary>
    [DataField]
    public bool Finisher;

    /// <summary>More weapon damage dice on this Strike.</summary>
    [DataField]
    public int ExtraDice;

    /// <summary>Taunts the target, hit or miss.</summary>
    [DataField]
    public bool TauntsTarget;

    /// <summary>Only against a creature that has ignored the user's Taunt (Proud Nail).</summary>
    [DataField]
    public bool NeedsIgnoredTaunt;

    /// <summary>Recharges the user's Spellstrike, hit or miss.</summary>
    [DataField]
    public bool RechargesSpellstrike;

    /// <summary>Reloads the held weapon as part of this Strike, a slinger's reload.</summary>
    [DataField]
    public bool Reloads;

    /// <summary>An unstable action: needs the innovation in use, and risks it breaking down.</summary>
    [DataField]
    public bool Unstable;

    /// <summary>Added to the attack roll.</summary>
    [DataField]
    public int AttackBonus;

    /// <summary>The target's concealed condition does not call for a flat check (Hunter's Aim).</summary>
    [DataField]
    public bool IgnoresConcealed;

    /// <summary>True when the effects need the Strike to deal damage, and not only to hit.</summary>
    [DataField]
    public bool EffectsNeedDamage = true;

    /// <summary>Strikes the same target a second time, at -2 unless the weapon is agile.</summary>
    [DataField]
    public bool Twice;

    /// <summary>Extra damage for each weapon die.</summary>
    [DataField]
    public int BonusPerDie;

    /// <summary>The target must not be the one the last Strike went at.</summary>
    [DataField]
    public bool DifferentTarget;

    /// <summary>
    /// The user rushes up to this many feet toward the target before the Strike, which stands in for the Stride or Leap in
    /// actions such as Defensive Advance and Flying Kick. Give the action a <c>TargetAction</c> range to match.
    /// </summary>
    [DataField]
    public int ClosesFeet;

    /// <summary>The user hops this many feet away from the target before the Strike, as Parting Shot's Step.</summary>
    [DataField]
    public int StepsBackFeet;

    /// <summary>Raises a held shield first, at no extra cost (Defensive Advance).</summary>
    [DataField]
    public bool RaisesShield;

    /// <summary>The target is off-guard against this Strike (Parting Shot).</summary>
    [DataField]
    public bool TargetOffGuard;

    /// <summary>
    /// Quick Draw: with no weapon in hand, draws one worn on the belt, back or neck into a free hand first, at no extra
    /// cost, and Strikes with it, melee or ranged.
    /// </summary>
    [DataField]
    public bool DrawsWeapon;

    /// <summary>
    /// Furious Finish: needs a Rage, adds the rounds it has left to the damage, up to 10, then ends it and leaves the user
    /// fatigued.
    /// </summary>
    [DataField]
    public bool EndsRage;

    /// <summary>A second Strike with the same weapon, at another enemy. Which one is up to <see cref="CFSecondStrike"/>.</summary>
    [DataField]
    public CFSecondStrike Second = CFSecondStrike.None;

    /// <summary>The action cannot be used unless there is someone for <see cref="Second"/> to go at (Quick Reversal).</summary>
    [DataField]
    public bool NeedsSecond;

    /// <summary>The second Strike does not add to the multiple attack penalty (Quick Reversal).</summary>
    [DataField]
    public bool SecondIsFree;

    /// <summary>Needs a melee weapon held in both hands, as Brutish Shove does.</summary>
    [DataField]
    public bool TwoHandedWeapon;

    /// <summary>Needs a hand free, as Combat Grab does to take hold.</summary>
    [DataField]
    public bool NeedsFreeHand;

    /// <summary>The press trait: only after another attack this turn, so while the multiple attack penalty is running.</summary>
    [DataField]
    public bool Press;

    /// <summary>Runs on the target after a miss that is not a critical miss.</summary>
    [DataField]
    public List<CFEffect> OnFailure = new();

    /// <summary>Runs on the target after a hit.</summary>
    [DataField]
    public List<CFEffect> OnHit = new();

    /// <summary>The weapon in the active hand has to be of one of these groups, as Leech-Clip needs a flail. Empty means any.</summary>
    [DataField]
    public List<CFWeaponGroup> Groups = new();

    /// <summary>Runs instead of <see cref="OnHit"/> on a critical hit, if it has anything in it.</summary>
    [DataField]
    public List<CFEffect> OnCritical = new();
}

/// <summary>
/// Who an action's second Strike goes at. It is made at the same multiple attack penalty as the first. The rules roll one
/// attack and one damage for both targets of a Swipe: here each gets its own roll.
/// </summary>
public enum CFSecondStrike : byte
{
    None,

    /// <summary>An enemy beside the first target and within the user's reach (Swipe).</summary>
    BesideTarget,

    /// <summary>An enemy beside the first target and straight behind it (Impaling Finisher).</summary>
    BehindTarget,

    /// <summary>Another enemy within the user's reach (Quick Reversal).</summary>
    OtherInReach,

    /// <summary>With a ranged weapon: the nearest other enemy in sight within 60 feet (Double Shot). It needs a second arrow.</summary>
    OtherInRange,
}
