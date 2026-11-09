using Content.Shared.CF.Companions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Content.Shared.CF.ZLevels;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character.Grants;

/// <summary>Raises proficiency ranks. A rank the character already beats is left alone.</summary>
public sealed partial class CFGrantProficiency : CFGrant
{
    /// <summary>
    /// What is raised: a save, <c>Perception</c>, a skill, a name from a class's <c>attacks:</c> or <c>defenses:</c> list,
    /// a <see cref="CFWeaponGroup"/>, <see cref="CFDefense.ClassDcProficiency"/>, or <see cref="CFDefense.SpellProficiency"/>.
    /// </summary>
    [DataField(required: true)]
    public List<string> Proficiencies = new();

    [DataField(required: true)]
    public CFProficiencyRank Rank;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        foreach (var proficiency in Proficiencies)
        {
            features.RaiseProficiency(args.Mob, proficiency, Rank);
        }
    }
}

/// <summary>A lasting bonus. It stacks by the usual rules, so a status bonus here does not add to a status spell.</summary>
public sealed partial class CFGrantBonus : CFGrant
{
    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField(required: true)]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set with <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? Skill;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFBonusSystem>().AddPermanentBonus(args.Mob, Stat, Type, Value, Trait, Skill?.Id);
    }
}

/// <summary>A lasting bonus that only counts while raging, with panache, or with no armor on.</summary>
public sealed partial class CFGrantWhile : CFGrant
{
    [DataField(required: true)]
    public CFBonusWhile While;

    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField(required: true)]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set with <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? Skill;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFWhileBonusSystem>().Add(args.Mob, new CFWhileBonus
        {
            While = While,
            Stat = Stat,
            Type = Type,
            Value = Value,
            Trait = Trait,
            Skill = Skill?.Id,
        });
    }
}

/// <summary>Gives the creature an aura, or adds to the one it has. With no effects it is an aura that does nothing yet.</summary>
public sealed partial class CFGrantAura : CFGrant
{
    [DataField]
    public List<CFAuraEffect> Effects = new();

    /// <summary>Makes the aura at least this wide, in feet.</summary>
    [DataField]
    public int SizeFeet;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFAuraSystem>().Add(args.Mob, Effects, SizeFeet);
    }
}

/// <summary>What a Barbarian's instinct does to Rage.</summary>
public sealed partial class CFGrantRage : CFGrant
{
    /// <summary>Replaces the 2 that Rage adds to melee damage. 0 leaves it alone.</summary>
    [DataField]
    public int Damage;

    /// <summary>The die of an unarmed attack the creature has while raging. 0 means none.</summary>
    [DataField]
    public int AttackDie;

    [DataField]
    public string AttackType = string.Empty;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().SetRage(args.Mob, Damage, AttackDie, AttackType);
    }
}

/// <summary>Makes one result of a save a degree better, such as a success against fear becoming a critical success.</summary>
public sealed partial class CFGrantSaveUpgrade : CFGrant
{
    /// <summary>Null means every save.</summary>
    [DataField]
    public CFDefenseType? Save;

    /// <summary>When set, it only works against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>The result that gets better.</summary>
    [DataField]
    public CFDegree From = CFDegree.Success;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFBonusSystem>().AddSaveUpgrade(args.Mob, Save, Trait, From);
    }
}

/// <summary>A lasting resistance. Set <see cref="Amount"/>, or <see cref="HalfLevel"/> for one that grows.</summary>
public sealed partial class CFGrantResistance : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField]
    public int Amount;

    /// <summary>Half the creature's level, at least 1, worked out when damage lands.</summary>
    [DataField]
    public bool HalfLevel;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFBonusSystem>().AddPermanentResistance(args.Mob, DamageType, Amount, HalfLevel);
    }
}

/// <summary>Weapons a character's people use. For proficiency they count as simpler than they are.</summary>
public sealed partial class CFGrantWeaponFamiliarity : CFGrant
{
    /// <summary>These count as simple weapons, and the character is at least trained with them.</summary>
    [DataField]
    public List<EntProtoId> Weapons = new();

    /// <summary>A weapon with one of these traits counts as one category simpler: advanced as martial, martial as simple.</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().AddFamiliarity(args.Mob, Weapons, Traits);
    }
}

/// <summary>A sense beyond normal sight.</summary>
public sealed partial class CFGrantSense : CFGrant
{
    [DataField(required: true)]
    public CFSense Sense;

    /// <summary>How far an imprecise sense reaches. Vision ignores this.</summary>
    [DataField]
    public int RangeFeet;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantSense(args.Mob, Sense, RangeFeet);
    }
}

/// <summary>
/// A spell from outside the character's slots. It needs a spell action entity. A cantrip is at will, and so is a ranked
/// spell on a creature with no slots of that rank, because daily uses and focus points do not exist yet (roadmap 1f).
/// </summary>
public sealed partial class CFGrantSpell : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFSpellPrototype> Spell;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantSpell(args.Mob, Spell);
    }
}

/// <summary>A focus spell. It adds a Focus Point to the character's pool, up to 3, and the Refocus action.</summary>
public sealed partial class CFGrantFocusSpell : CFGrant
{
    [DataField(required: true)]
    public ProtoId<CFSpellPrototype> Spell;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantFocusSpell(args.Mob, Spell);
    }
}

/// <summary>Focus Points with no focus spell attached, as a Psychic's, and the Refocus action.</summary>
public sealed partial class CFGrantFocusPoints : CFGrant
{
    [DataField]
    public int Count = 1;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().AddFocusPoints(args.Mob, Count);
    }
}

/// <summary>A resistance that counts while the character wears their armor innovation.</summary>
public sealed partial class CFGrantInnovationResistance : CFGrant
{
    [DataField(required: true)]
    public CFInnovationResistance Resistance = default!;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFInventorSystem>().AddResistance(args.Mob, Resistance);
    }
}

/// <summary>What a Necromancer's thralls are like.</summary>
public sealed partial class CFGrantThralls : CFGrant
{
    /// <summary>Thralls made per casting of Create Thrall, beyond the usual two.</summary>
    [DataField]
    public int Extra;

    [DataField]
    public bool BloodReturns;

    [DataField]
    public int ExtraSpeed;

    /// <summary>Replaces the thralls' damage type.</summary>
    [DataField]
    public string DamageType = string.Empty;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var necromancer = entities.EnsureComponent<CFNecromancerComponent>(args.Mob);
        necromancer.ExtraThralls += Extra;
        necromancer.BloodReturns |= BloodReturns;
        necromancer.ExtraSpeed += ExtraSpeed;
        if (DamageType.Length > 0)
            necromancer.DamageType = DamageType;
    }
}

/// <summary>Gear placed at the character's feet.</summary>
public sealed partial class CFGrantItem : CFGrant
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public int Count = 1;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var coordinates = entities.GetComponent<TransformComponent>(args.Mob).Coordinates;
        for (var i = 0; i < Count; i++)
        {
            entities.SpawnAtPosition(Item, coordinates);
        }
    }
}

/// <summary>An action on the wheel or hotbar.</summary>
public sealed partial class CFGrantAction : CFGrant
{
    [DataField(required: true)]
    public EntProtoId Action;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantAction(args.Mob, Action);
    }
}

/// <summary>A natural attack, such as jaws or claws. It replaces the fist, unless the character already has a bigger die.</summary>
public sealed partial class CFGrantUnarmedAttack : CFGrant
{
    [DataField]
    public int Dice = 1;

    [DataField(required: true)]
    public int Die;

    [DataField(required: true)]
    public string DamageType = string.Empty;

    [DataField]
    public bool Finesse;

    [DataField]
    public bool Agile;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFCharacterFeatureSystem>().GrantUnarmedAttack(args.Mob, Dice, Die, DamageType, Finesse, Agile);
    }
}

/// <summary>Falling never hurts this character.</summary>
public sealed partial class CFGrantSafeFall : CFGrant
{
    /// <summary>It only works with a hand free.</summary>
    [DataField]
    public bool NeedsFreeHand;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.EnsureComponent<CFSafeFallComponent>(args.Mob).NeedsFreeHand = NeedsFreeHand;
    }
}

/// <summary>A reaction: what sets it off and what it does. It fires by itself. See <see cref="CFReaction"/>.</summary>
public sealed partial class CFGrantReaction : CFGrant
{
    [DataField(required: true)]
    public CFReaction Reaction = default!;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFReactionSystem>().Add(args.Mob, Reaction);
    }
}

/// <summary>Sets a flag or mark on <see cref="CFCombatOptionsComponent"/>, for behaviour that is already coded.</summary>
public sealed partial class CFGrantOption : CFGrant
{
    [DataField]
    public CFCombatOption? Option;

    /// <summary>What pointing at an enemy does for this character.</summary>
    [DataField]
    public CFPointMark? PointMark;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (Option is { } option)
            features.SetOption(args.Mob, option);

        if (PointMark is { } mark)
            features.SetPointMark(args.Mob, mark);
    }
}
