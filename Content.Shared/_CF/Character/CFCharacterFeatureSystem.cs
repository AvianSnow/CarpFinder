using Content.Shared.Actions;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Content.Shared.Overlays;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>
/// Runs the <see cref="CFGrant"/> lists of a character's options, at spawn and on level-up.
/// Grants are the one way an option changes a character: add a <c>grants</c> entry, not a case in code.
/// </summary>
public sealed partial class CFCharacterFeatureSystem : EntitySystem
{
    /// <summary>How much low-light vision brightens the dark. Darkvision is about twice this.</summary>
    private static readonly Color LowLightColor = new((byte) 0x9B, (byte) 0xB6, (byte) 0xFF, (byte) 0x18);

    private static readonly Color DarkvisionColor = new((byte) 0x9B, (byte) 0xB6, (byte) 0xFF, (byte) 0x30);

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    /// <summary>
    /// Runs one option's grants. <paramref name="source"/> names the option, and a source that has run does nothing,
    /// so gaining the same option a second way does not double its bonuses or its gear.
    /// </summary>
    public void Apply(EntityUid mob, IReadOnlyList<CFGrant> grants, string source)
    {
        if (grants.Count == 0)
            return;

        if (!EnsureComp<CFCharacterFeaturesComponent>(mob).Applied.Add(source))
            return;

        var args = new CFGrantArgs(mob, source);
        foreach (var grant in grants)
        {
            grant.Apply(EntityManager, args);
        }
    }

    /// <summary>Every class feature gained by <paramref name="level"/> that has not run yet.</summary>
    public void ApplyClassFeatures(EntityUid mob, CFClassPrototype cls, int level)
    {
        foreach (var feature in cls.Features)
        {
            if (feature.Level > level)
                continue;

            Apply(mob, feature.Grants, $"class:{cls.ID}:{feature.Level}:{feature.Name}");
            if (feature.Choices.Count > 0)
                ApplyOptionPicks(mob, CFOptionChoices.FeatureSource(cls, feature));
        }
    }

    /// <summary>A subclass pick's level 1 grants, and every later feature of it reached by <paramref name="level"/>.</summary>
    public void ApplyClassChoice(EntityUid mob, CFClassChoicePrototype choice, int level)
    {
        Apply(mob, choice.Grants, $"choice:{choice.ID}");
        foreach (var feature in choice.Features)
        {
            if (feature.Level <= level)
                Apply(mob, feature.Grants, $"choice:{choice.ID}:{feature.Level}:{feature.Name}");
        }
    }

    public void RaiseProficiency(EntityUid mob, string proficiency, CFProficiencyRank rank)
    {
        var combat = EnsureComp<CFCombatantComponent>(mob);
        if (combat.ProficiencyRanks.GetValueOrDefault(proficiency) >= rank)
            return;

        combat.ProficiencyRanks[proficiency] = rank;
        Dirty(mob, combat);
    }

    /// <summary>What a Barbarian's instinct does to Rage. Only what is set changes.</summary>
    public void SetRage(EntityUid mob, int damage, int attackDie, string attackType)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        if (damage > 0)
            options.RageDamage = damage;

        if (attackDie > 0)
        {
            options.RageAttackDie = attackDie;
            options.RageAttackType = attackType;
        }

        Dirty(mob, options);
    }

    public void AddFamiliarity(EntityUid mob, List<EntProtoId> weapons, List<ProtoId<CFTraitPrototype>> traits)
    {
        var combat = EnsureComp<CFCombatantComponent>(mob);
        foreach (var weapon in weapons)
        {
            if (!combat.FamiliarWeapons.Contains(weapon))
                combat.FamiliarWeapons.Add(weapon);
        }

        foreach (var trait in traits)
        {
            if (!combat.FamiliarWeaponTraits.Contains(trait))
                combat.FamiliarWeaponTraits.Add(trait);
        }

        Dirty(mob, combat);
    }

    public void GrantSense(EntityUid mob, CFSense sense, int rangeFeet = 0)
    {
        var senses = EnsureComp<CFSensesComponent>(mob);
        switch (sense)
        {
            case CFSense.Darkvision:
                senses.Darkvision = true;
                SetVision(mob, DarkvisionColor);
                break;
            case CFSense.LowLightVision:
                senses.LowLightVision = true;
                if (!senses.Darkvision)
                    SetVision(mob, LowLightColor);
                break;
            default:
                senses.ImpreciseFeet = Math.Max(senses.ImpreciseFeet, rangeFeet);
                break;
        }

        Dirty(mob, senses);
    }

    private void SetVision(EntityUid mob, Color color)
    {
        var vision = EnsureComp<NightVisionComponent>(mob);
        vision.Enabled = true;
        vision.Prioritized = true;
        vision.LightingColor = color;
        vision.NoiseAmount = 0f;
        Dirty(mob, vision);
    }

    /// <summary>Adds a spell's action, if the character does not have it yet. False if the spell has no action entity.</summary>
    public bool GrantSpell(EntityUid mob, ProtoId<CFSpellPrototype> spell)
    {
        var action = "CF" + spell.Id;
        if (!_prototypes.HasIndex<EntityPrototype>(action))
            return false;

        GrantAction(mob, action);
        return true;
    }

    public void GrantFocusSpell(EntityUid mob, ProtoId<CFSpellPrototype> spell)
    {
        GrantSpell(mob, spell);
        var book = EnsureComp<CFSpellbookComponent>(mob);
        book.FocusPoints = Math.Min(CFFocusSystem.MaxFocusPoints, book.FocusPoints + 1);
        Dirty(mob, book);
        GrantAction(mob, "CFActRefocus");
    }

    /// <summary>Adds an action, unless another option has already granted it.</summary>
    public void GrantAction(EntityUid mob, EntProtoId action)
    {
        foreach (var existing in _actions.GetActions(mob))
        {
            if (MetaData(existing).EntityPrototype?.ID == action.Id)
                return;
        }

        _actions.AddAction(mob, action);
    }

    /// <summary>Gives what the player chose for an option, once that option is gained.</summary>
    public void ApplyOptionPicks(EntityUid mob, string source)
    {
        if (!TryComp(mob, out CFExperienceComponent? xp))
            return;

        foreach (var pick in xp.OptionPicks)
        {
            if (pick.Source != source)
                continue;

            if (_prototypes.HasIndex<CFSpellPrototype>(pick.Value))
            {
                GrantSpell(mob, pick.Value);
                continue;
            }

            if (Enum.IsDefined(typeof(CFWeaponGroup), pick.Value))
            {
                RaiseProficiency(mob, pick.Value, CFProficiencyRank.Master);
                continue;
            }

            var combat = EnsureComp<CFCombatantComponent>(mob);
            if (combat.TrainedSkills.Contains(pick.Value))
                continue;

            combat.TrainedSkills.Add(pick.Value);
            Dirty(mob, combat);
        }
    }

    /// <summary>A natural attack replaces the fist, but a smaller one does not replace a bigger one the character has.</summary>
    public void GrantUnarmedAttack(EntityUid mob, int dice, int die, string damageType, bool finesse, bool agile)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        if (die <= options.NaturalDie)
            return;

        options.NaturalDice = dice;
        options.NaturalDie = die;
        options.NaturalType = damageType;
        options.NaturalFinesse = finesse;
        options.NaturalAgile = agile;
        Dirty(mob, options);
    }

    /// <summary>Lights an action up on the action bar, or turns it off, by its prototype.</summary>
    public void SetActionToggled(EntityUid mob, EntProtoId action, bool toggled)
    {
        foreach (var existing in _actions.GetActions(mob))
        {
            if (MetaData(existing).EntityPrototype?.ID != action.Id)
                continue;

            _actions.SetToggled((existing, existing.Comp), toggled);
            return;
        }
    }

    public void SetPointMark(EntityUid mob, CFPointMark mark)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        options.PointMark = mark;
        Dirty(mob, options);
    }

    public void SetOption(EntityUid mob, CFCombatOption option)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        switch (option)
        {
            case CFCombatOption.SneakAttack:
                options.SneakAttack = true;
                break;
            case CFCombatOption.CanRage:
                options.CanRage = true;
                break;
            case CFCombatOption.ReactiveStrike:
                options.ReactiveStrike = true;
                break;
            case CFCombatOption.ShieldBlock:
                options.ShieldBlock = true;
                break;
            case CFCombatOption.Flurry:
                options.Flurry = true;
                break;
            case CFCombatOption.PowerfulFist:
                options.PowerfulFist = true;
                break;
            case CFCombatOption.PanacheStyle:
                options.PanacheStyle = true;
                break;
            case CFCombatOption.Spellstrike:
                options.Spellstrike = true;
                break;
            case CFCombatOption.Devise:
                options.Devise = true;
                break;
            case CFCombatOption.Exploit:
                options.Exploit = true;
                break;
            case CFCombatOption.Anthem:
                options.Anthem = true;
                break;
            case CFCombatOption.BattleMedicine:
                options.BattleMedicine = true;
                break;
            case CFCombatOption.ReactiveShield:
                options.ReactiveShield = true;
                break;
            case CFCombatOption.YoureNext:
                options.YoureNext = true;
                break;
            case CFCombatOption.TumbleBehind:
                options.TumbleBehind = true;
                break;
            case CFCombatOption.SnaggingStrike:
                options.SnaggingStrike = true;
                break;
            case CFCombatOption.ExactingStrike:
                options.ExactingStrike = true;
                break;
            case CFCombatOption.DoubleSlice:
                options.DoubleSlice = true;
                break;
            case CFCombatOption.TwinFeint:
                options.TwinFeint = true;
                break;
            case CFCombatOption.ViciousSwing:
                options.ViciousSwing = true;
                break;
            case CFCombatOption.AnyWeaponSneak:
                options.AnyWeaponSneak = true;
                break;
            case CFCombatOption.PreyPrecision:
                options.PreyPrecision = true;
                break;
            case CFCombatOption.PreyFlurry:
                options.PreyFlurry = true;
                break;
            case CFCombatOption.PreyOutwit:
                options.PreyOutwit = true;
                break;
            case CFCombatOption.Bravery:
                options.Bravery = true;
                break;
            case CFCombatOption.DenyAdvantage:
                options.DenyAdvantage = true;
                break;
            case CFCombatOption.DreadStriker:
                options.DreadStriker = true;
                break;
            case CFCombatOption.BrutalBeating:
                options.BrutalBeating = true;
                break;
            case CFCombatOption.UnbalancingBlow:
                options.UnbalancingBlow = true;
                break;
            case CFCombatOption.FarShot:
                options.FarShot = true;
                break;
            case CFCombatOption.StrongArm:
                options.StrongArm = true;
                break;
            case CFCombatOption.FreeStep:
                options.FreeStep = true;
                break;
            case CFCombatOption.FreeStepWithShield:
                options.FreeStepWithShield = true;
                break;
            case CFCombatOption.RagingThrower:
                options.RagingThrower = true;
                break;
            case CFCombatOption.CrushingGrab:
                options.CrushingGrab = true;
                break;
            case CFCombatOption.FinishingFollowThrough:
                options.FinishingFollowThrough = true;
                break;
            case CFCombatOption.StrategicStrike:
                options.StrategicStrike = true;
                break;
            case CFCombatOption.SorcerousPotency:
                options.SorcerousPotency = true;
                break;
            case CFCombatOption.PanacheManeuvers:
                options.PanacheManeuvers = true;
                break;
            case CFCombatOption.SlingerPrecision:
                options.SlingerPrecision = true;
                break;
            case CFCombatOption.PunishingShove:
                options.PunishingShove = true;
                break;
            case CFCombatOption.HumbleStrikes:
                options.HumbleStrikes = true;
                break;
            case CFCombatOption.GuardianArmor:
                options.GuardianArmor = true;
                break;
            case CFCombatOption.ImplementEmpowerment:
                options.ImplementEmpowerment = true;
                break;
            case CFCombatOption.WeaponImplement:
                options.WeaponImplement = true;
                break;
            case CFCombatOption.HealingHands:
                options.HealingHands = true;
                break;
            case CFCombatOption.HarmingHands:
                options.HarmingHands = true;
                break;
            case CFCombatOption.ShakesOffFear:
                options.ShakesOffFear = true;
                break;
            case CFCombatOption.IgnoresArmorSpeed:
                options.IgnoresArmorSpeed = true;
                break;
            case CFCombatOption.FinesseDexterity:
                options.FinesseDexterity = true;
                break;
        }

        Dirty(mob, options);
    }
}
