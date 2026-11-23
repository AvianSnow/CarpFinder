using Content.Shared.Actions;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Scent;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
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
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    /// <summary>The Speed that stock walking and running stand for, in feet.</summary>
    private const float BaselineSpeedFeet = 25f;

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

    /// <summary>
    /// Gives a feat: its skills, its natural attack if it beats the one the character has, its grants, and the picks made for
    /// it. A feat the character already has does nothing, so one granted by a heritage and also picked is not doubled.
    /// </summary>
    /// <param name="adjustSpeed">A Speed bonus is applied here on level-up. At spawn the build's Speed already counts it.</param>
    public void ApplyFeat(EntityUid mob, CFFeatPrototype feat, bool adjustSpeed = true)
    {
        if (!EnsureComp<CFCharacterFeaturesComponent>(mob).Applied.Add($"featTaken:{feat.ID}"))
            return;

        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        var combat = EnsureComp<CFCombatantComponent>(mob);

        foreach (var skill in feat.TrainedSkills)
        {
            if (!combat.TrainedSkills.Contains(skill.Id))
                combat.TrainedSkills.Add(skill.Id);
        }

        foreach (var lore in feat.Lores)
        {
            if (!combat.LoreNames.Contains(lore))
                combat.LoreNames.Add(lore);
        }

        if (adjustSpeed && feat.SpeedBonus > 0 && TryComp(mob, out MovementSpeedModifierComponent? movement))
        {
            combat.SpeedFeet += feat.SpeedBonus;
            var scale = feat.SpeedBonus / BaselineSpeedFeet;
            _speed.ChangeBaseSpeed(
                mob,
                movement.BaseWalkSpeed + 2.5f * scale,
                movement.BaseSprintSpeed + 4.5f * scale,
                movement.BaseAcceleration);
        }

        if (feat.NaturalDie > options.NaturalDie)
        {
            options.NaturalDice = Math.Max(1, feat.NaturalDice);
            options.NaturalDie = feat.NaturalDie;
            options.NaturalType = feat.NaturalDamage;
            options.NaturalFinesse = feat.NaturalFinesse;
            options.NaturalAgile = feat.NaturalAgile;
        }

        Dirty(mob, combat);
        Dirty(mob, options);
        Apply(mob, feat.Grants, $"feat:{feat.ID}");
        ApplyOptionPicks(mob, feat.ID);
    }

    /// <summary>Whether <see cref="ApplyFeat"/> has given this feat.</summary>
    public bool HasFeat(EntityUid mob, ProtoId<CFFeatPrototype> feat)
    {
        return CompOrNull<CFCharacterFeaturesComponent>(mob)?.Applied.Contains($"featTaken:{feat.Id}") == true;
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

    public void AddFamiliarity(EntityUid mob, List<EntProtoId> weapons, List<ProtoId<CFTraitPrototype>> traits, bool favored = false)
    {
        var combat = EnsureComp<CFCombatantComponent>(mob);
        foreach (var weapon in weapons)
        {
            if (!combat.FamiliarWeapons.Contains(weapon))
                combat.FamiliarWeapons.Add(weapon);

            if (favored && !combat.FavoredWeapons.Contains(weapon))
                combat.FavoredWeapons.Add(weapon);
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
            case CFSense.Scent:
                senses.ImpreciseFeet = Math.Max(senses.ImpreciseFeet, rangeFeet);
                EnsureComp<CFSmellerComponent>(mob);
                break;
            default:
                senses.ImpreciseFeet = Math.Max(senses.ImpreciseFeet, rangeFeet);
                break;
        }

        Dirty(mob, senses);
    }

    /// <summary>Darkvision for as long as a Rage lasts. Ending it puts back low-light vision, or none.</summary>
    public void SetRageVision(EntityUid mob, bool on)
    {
        if (on)
            SetVision(mob, DarkvisionColor);
        else if (CompOrNull<CFSensesComponent>(mob)?.LowLightVision == true)
            SetVision(mob, LowLightColor);
        else
            RemComp<NightVisionComponent>(mob);
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

    public void AddFocusPoints(EntityUid mob, int count)
    {
        var book = EnsureComp<CFSpellbookComponent>(mob);
        book.FocusPoints = Math.Min(CFFocusSystem.MaxFocusPoints, book.FocusPoints + count);
        Dirty(mob, book);
        GrantAction(mob, "CFActRefocus");
    }

    public void GrantFocusSpell(EntityUid mob, ProtoId<CFSpellPrototype> spell)
    {
        // The same spell from a second option adds no second Focus Point (Order Magic after the Untamed Form feat).
        foreach (var existing in _actions.GetActions(mob))
        {
            if (MetaData(existing).EntityPrototype?.ID == "CF" + spell.Id)
                return;
        }

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

            if (pick.Value.StartsWith(CFOptionChoices.SignaturePrefix, StringComparison.Ordinal))
            {
                var signature = pick.Value[CFOptionChoices.SignaturePrefix.Length..];
                if (TryComp(mob, out CFSpellbookComponent? book) && !book.SignatureSpells.Contains(signature))
                {
                    book.SignatureSpells.Add(signature);
                    Dirty(mob, book);
                }

                continue;
            }

            // Read by the grant that asked for it, such as CFGrantChosen.
            if (pick.Value.StartsWith(CFOptionChoices.NamedPrefix, StringComparison.Ordinal))
                continue;

            if (_prototypes.HasIndex<CFSpellPrototype>(pick.Value))
            {
                GrantSpell(mob, pick.Value);
                continue;
            }

            if (_prototypes.TryIndex(pick.Value, out CFFeatPrototype? feat))
            {
                ApplyFeat(mob, feat, adjustSpeed: false);
                continue;
            }

            // Read when it is needed, by CFClassChoices.KeyAbility.
            if (Enum.IsDefined(typeof(CFAbility), pick.Value))
                continue;

            // Read by CFGrantDomainSpell.
            if (_prototypes.HasIndex<CFDomainPrototype>(pick.Value))
                continue;

            // Read by CFGrantFavoredTerrain.
            if (Enum.IsDefined(typeof(Content.Shared.CF.Terrain.CFFavoredTerrain), pick.Value))
                continue;

            // Read by the grant that asked for it, such as CFGrantChosenResistance.
            if (_prototypes.HasIndex<CFDamageTypePrototype>(pick.Value))
                continue;

            if (pick.Value is "Fortitude" or "Reflex" or "Will" or "Perception")
            {
                RaiseProficiency(mob, pick.Value, CFProficiencyRank.Expert);
                continue;
            }

            if (_prototypes.HasIndex<CFLanguagePrototype>(pick.Value))
            {
                var languages = EnsureComp<CFLanguagesComponent>(mob);
                if (!languages.Known.Contains(pick.Value))
                {
                    languages.Known.Add(pick.Value);
                    Dirty(mob, languages);
                }

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
    public void GrantUnarmedAttack(EntityUid mob, int dice, int die, string damageType, bool finesse, bool agile, int deadly = 0, string versatile = "")
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        if (die <= options.NaturalDie)
            return;

        options.NaturalDice = dice;
        options.NaturalDie = die;
        options.NaturalType = damageType;
        options.NaturalFinesse = finesse;
        options.NaturalAgile = agile;
        options.NaturalDeadly = deadly;
        options.NaturalVersatile = versatile;
        Dirty(mob, options);
    }

    /// <summary>Gives the natural attack the creature already has a trait, as Dogtooth Tengu makes the beak deadly.</summary>
    public void AddUnarmedTraits(EntityUid mob, int deadly, string versatile)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        if (deadly > 0)
            options.NaturalDeadly = Math.Max(options.NaturalDeadly, deadly);

        if (versatile.Length > 0)
            options.NaturalVersatile = versatile;

        Dirty(mob, options);
    }

    /// <summary>The id a grant's source stores its option picks under: <c>heritage:X</c> and <c>feat:X</c> are <c>X</c>.</summary>
    public static string PickSource(string source)
    {
        // A class feature's grants run as class:Fighter:3:Name, and its picks are kept as class:Fighter:Name.
        var parts = source.Split(':');
        if (parts.Length >= 4 && parts[0] == "class" && int.TryParse(parts[2], out _))
            return $"class:{parts[1]}:{string.Join(':', parts[3..])}";

        foreach (var prefix in new[] { "heritage:", "feat:", "choice:", "ancestry:", "background:" })
        {
            if (source.StartsWith(prefix, StringComparison.Ordinal))
                return source[prefix.Length..];
        }

        return source;
    }

    /// <summary>What the player picked for an option, or null.</summary>
    public string? Pick(EntityUid mob, string source, int slot = 0)
    {
        if (!TryComp(mob, out CFExperienceComponent? xp))
            return null;

        var id = PickSource(source);
        foreach (var pick in xp.OptionPicks)
        {
            if (pick.Source == id && pick.Slot == slot)
                return pick.Value;
        }

        return null;
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

    /// <summary>
    /// The level of the option a grant came from: a feat's level, or the level in a class feature's source
    /// (<c>class:Fighter:3:Name</c>). Anything else, such as an ancestry or a heritage, is level 1.
    /// </summary>
    public int SourceLevel(string source)
    {
        var parts = source.Split(':');
        if (parts.Length >= 3 && int.TryParse(parts[2], out var level))
            return level;

        if (parts.Length == 2 && parts[0] == "feat" && _prototypes.TryIndex<CFFeatPrototype>(parts[1], out var feat))
            return feat.Level;

        return 1;
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
            case CFCombatOption.Diehard:
                options.Diehard = true;
                break;
            case CFCombatOption.HeftyHauler:
                options.HeftyHauler = true;
                break;
            case CFCombatOption.NineLives:
                options.NineLives = true;
                break;
            case CFCombatOption.ToughToKill:
                options.ToughToKill = true;
                break;
            case CFCombatOption.StrongBlooded:
                options.StrongBlooded = true;
                break;
            case CFCombatOption.EasierBurning:
                options.PersistentFireDc = 10;
                break;
            case CFCombatOption.EasierBleeding:
                options.PersistentBleedDc = 10;
                break;
            case CFCombatOption.DefyDeath:
                options.DefyDeath = true;
                break;
            case CFCombatOption.Toughness:
                options.Toughness = true;
                break;
            case CFCombatOption.SecondWind:
                options.SecondWind = true;
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
            case CFCombatOption.ScoundrelFeint:
                options.ScoundrelFeint = true;
                break;
            case CFCombatOption.DistractingFeint:
                options.DistractingFeint = true;
                break;
            case CFCombatOption.TwinDistraction:
                options.TwinDistraction = true;
                break;
            case CFCombatOption.CommunalHealing:
                options.CommunalHealing = true;
                break;
            case CFCombatOption.PanicTheDead:
                options.PanicTheDead = true;
                break;
            case CFCombatOption.SapLife:
                options.SapLife = true;
                break;
            case CFCombatOption.DirectedChannel:
                options.DirectedChannel = true;
                break;
            case CFCombatOption.Cruelty:
                options.Cruelty = true;
                break;
            case CFCombatOption.BlessedShield:
                options.BlessedShield = true;
                break;
            case CFCombatOption.VersatilePerformance:
                options.VersatilePerformance = true;
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
            case CFCombatOption.DeadlySimplicity:
                options.DeadlySimplicity = true;
                break;
            case CFCombatOption.AggressiveBlock:
                options.AggressiveBlock = true;
                break;
            case CFCombatOption.GuardedMovement:
                options.GuardedMovement = true;
                break;
            case CFCombatOption.DancingLeaf:
                options.DancingLeaf = true;
                break;
            case CFCombatOption.StunningBlows:
                options.StunningBlows = true;
                break;
            case CFCombatOption.ScalpelsPoint:
                options.ScalpelsPoint = true;
                break;
            case CFCombatOption.ElegantBuckler:
                options.ElegantBuckler = true;
                break;
            case CFCombatOption.ShieldWarfare:
                options.ShieldWarfare = true;
                break;
            case CFCombatOption.QuickTempered:
                options.QuickTempered = true;
                break;
            case CFCombatOption.SurpriseAttack:
                options.SurpriseAttack = true;
                break;
            case CFCombatOption.MastermindRecall:
                options.MastermindRecall = true;
                break;
            case CFCombatOption.MonsterHunter:
                options.MonsterHunter = true;
                break;
            case CFCombatOption.KnownWeaknesses:
                options.KnownWeaknesses = true;
                break;
            case CFCombatOption.EmotionalPush:
                options.EmotionalPush = true;
                break;
            case CFCombatOption.ThoughtformSummoning:
                options.ThoughtformSummoning = true;
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
            case CFCombatOption.ExpertOverdrive:
                options.ExpertOverdrive = true;
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
