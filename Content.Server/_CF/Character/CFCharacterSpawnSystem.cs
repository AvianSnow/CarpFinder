using System.Linq;
using Content.Shared.CF.Languages;
using Content.Server.CF.GOAP.Influence;
using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Shop;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Content.Server.CF.Companions;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Character;

/// <summary>
/// Applies Carpfinder build data from the character profile when a player spawns.
/// </summary>
public sealed partial class CFCharacterSpawnSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFActionPoolSystem _actionPool = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFCompanionSystem _companions = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;

    /// <summary>Carpfinder Speed of 25 feet is the usual human walk.</summary>
    private const float BaselineSpeedFeet = 25f;

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        ApplyBuild(args.Mob, args.Profile);
        GrantPurse(args.Mob, args.Profile, args.JobId);
        _factions.AssignJob(args.Mob, args.JobId);
        SpawnCompanion(args.Mob, args.Profile);
    }

    /// <summary>
    /// A class feature or chosen feat that grants a companion spawns that body beside the character.
    /// Chosen pet gear is placed on the same tile. Role loadout items are equipped by the station spawner.
    /// </summary>
    private void SpawnCompanion(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile)
    {
        var build = profile.CFBuild;
        CFPetPrototype? pet = null;
        if (!string.IsNullOrEmpty(build.PetId)
            && _prototypes.TryIndex<CFPetPrototype>(build.PetId, out var saved)
            && CFPetMath.BuildGrants(_prototypes, build, saved.Kind))
        {
            pet = saved;
        }
        else if (CFPetMath.BuildGrants(_prototypes, build, CFPetKind.Companion))
        {
            pet = FirstPet(CFPetKind.Companion);
        }
        else if (CFPetMath.BuildGrants(_prototypes, build, CFPetKind.Familiar))
        {
            pet = FirstPet(CFPetKind.Familiar);
        }

        if (pet == null)
            return;

        var prototype = _prototypes.HasIndex<EntityPrototype>(pet.Entity) ? pet.Entity : "CFSummon";
        var companion = Spawn(prototype, Transform(mob).Coordinates.Offset(new Vector2(1f, 0f)));
        _companions.Bind(mob, companion);
        if (!string.IsNullOrWhiteSpace(build.PetName))
            _meta.SetEntityName(companion, build.PetName.Trim());

        var sheet = CFAttributeMath.Snapshot(_prototypes, profile);
        var block = CFPetMath.Present(pet, build, sheet);
        var combat = EnsureComp<CFCombatantComponent>(companion);
        combat.Level = block.Level;
        combat.ArmorClass = block.ArmorClass;
        combat.MaxHp = block.HitPoints;
        combat.CurrentHp = block.HitPoints;
        Dirty(companion, combat);
        _companions.ComeAlong(companion);

        foreach (var gearId in build.PetGear)
        {
            if (!_prototypes.TryIndex<CFPetGearPrototype>(gearId, out var gear))
                continue;
            if (!_prototypes.HasIndex<EntityPrototype>(gear.Entity))
                continue;

            Spawn(gear.Entity, Transform(mob).Coordinates);
        }
    }

    private CFPetPrototype? FirstPet(CFPetKind kind)
    {
        CFPetPrototype? first = null;
        foreach (var pet in _prototypes.EnumeratePrototypes<CFPetPrototype>())
        {
            if (pet.Kind != kind)
                continue;
            if (first == null || string.Compare(pet.Name, first.Name, StringComparison.Ordinal) < 0)
                first = pet;
        }

        return first;
    }

    private void GrantPurse(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile, string? jobId)
    {
        var purse = EnsureComp<CFPurseComponent>(mob);
        purse.Copper = CFCoins.Remaining(profile, jobId, _prototypes);
        purse.Seeded = true;
        Dirty(mob, purse);
    }

    public void ApplyBuild(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile)
    {
        var sheet = CFAttributeMath.Snapshot(_prototypes, profile);

        var abilities = EnsureComp<CFAbilityScoreComponent>(mob);
        abilities.AbilityModifiers = new Dictionary<CFAbility, int>(sheet.Modifiers);
        Dirty(mob, abilities);

        var combatant = EnsureComp<CFCombatantComponent>(mob);
        combatant.Level = 1;
        combatant.ArmorFromEquipment = true;
        combatant.MaxHp = sheet.MaxHp;
        combatant.CurrentHp = sheet.MaxHp;
        combatant.ClassId = profile.CFBuild.Class?.Id;
        combatant.BackgroundId = profile.CFBuild.Background?.Id;
        combatant.KeyAbility = sheet.Class == null ? null : CFClassChoices.KeyAbility(_prototypes, profile.CFBuild, sheet.Class);
        _armor.RefreshArmorClass(mob);
        var ancestryId = sheet.Ancestry?.ID;
        var classId = sheet.Class?.ID;
        var chosenFeats = CFSkills.SelectedFeats(_prototypes, profile.CFBuild, ancestryId, classId, 1);
        var speedBonus = 0;
        combatant.TrainedSkills = CFSkills.CollectTrainedSkills(_prototypes, profile.CFBuild, ancestryId, classId, 1);
        var xp = EnsureComp<CFExperienceComponent>(mob);
        xp.Xp = 0;
        xp.AncestryFeat = IdOf(profile.CFBuild.AncestryFeat);
        xp.ClassFeat = IdOf(profile.CFBuild.ClassFeat);
        xp.ClassFeatLevel2 = IdOf(profile.CFBuild.ClassFeatLevel2);
        xp.ClassFeatLevel4 = IdOf(profile.CFBuild.ClassFeatLevel4);
        xp.AncestryFeatLevel5 = IdOf(profile.CFBuild.AncestryFeatLevel5);
        xp.SkillFeatLevel2 = QualifiedFeat(profile.CFBuild, ancestryId, profile.CFBuild.SkillFeatLevel2, 2, skillSlot: true);
        xp.GeneralFeatLevel3 = QualifiedFeat(profile.CFBuild, ancestryId, profile.CFBuild.GeneralFeatLevel3, 3, skillSlot: false);
        xp.SkillFeatLevel4 = QualifiedFeat(profile.CFBuild, ancestryId, profile.CFBuild.SkillFeatLevel4, 4, skillSlot: true);
        xp.SkillIncreaseLevel3 = KnownSkill(profile.CFBuild.SkillIncreaseLevel3);
        xp.SkillIncreaseLevel5 = KnownSkill(profile.CFBuild.SkillIncreaseLevel5);
        xp.AbilityBoostsLevel5 = profile.CFBuild.AbilityBoostsLevel5.Distinct().Take(CFAttributeMath.LevelFiveBoostCount).ToList();
        xp.ClassChoices = CFClassChoices.Selected(_prototypes, profile.CFBuild).Select(choice => choice.ID).ToList();
        Dirty(mob, xp);
        combatant.LoreNames.Clear();
        foreach (var chosen in chosenFeats)
        {
            speedBonus += Math.Max(0, chosen.SpeedBonus);
            foreach (var lore in chosen.Lores)
            {
                if (!combatant.LoreNames.Contains(lore))
                    combatant.LoreNames.Add(lore);
            }
        }
        combatant.TemporaryHp = 0;
        combatant.Dying = 0;
        combatant.Wounded = 0;
        Dirty(mob, combatant);
        CFHeritagePrototype? heritage = null;
        if (profile.CFBuild.Heritage != null)
            _prototypes.TryIndex(profile.CFBuild.Heritage.Value, out heritage);
        if (heritage != null && !heritage.Fits(sheet.Ancestry?.ID))
            heritage = null;
        ApplyAncestry(mob, sheet.Ancestry, heritage, speedBonus);
        var languages = EnsureComp<CFLanguagesComponent>(mob);
        languages.Known = CFLanguageRules.Known(_prototypes, sheet.Ancestry, profile.CFBuild.Languages, sheet.Modifiers);
        Dirty(mob, languages);
        _actionPool.Ensure(mob);
        ApplySpellbook(mob, profile, sheet.Class);
        GrantSpellActions(mob, profile);
        ApplyFeatures(mob, profile, sheet.Ancestry, heritage, sheet.Class, chosenFeats);
    }

    /// <summary>
    /// Speed scales the usual walk and sprint. Darkvision uses the same night-vision overlay as goblins.
    /// </summary>
    /// <summary>The feat, if it fits the slot and the build meets its skill requirement by that level.</summary>
    private string? QualifiedFeat(
        CFCharacterBuildData build,
        string? ancestryId,
        ProtoId<CFFeatPrototype>? id,
        int level,
        bool skillSlot)
    {
        if (id == null || !_prototypes.TryIndex(id.Value, out var feat))
            return null;

        var fits = skillSlot ? feat.FitsSkillSlot(level) : feat.FitsGeneralSlot(level);
        return fits && CFSkills.Qualifies(_prototypes, build, ancestryId, feat, level) ? feat.ID : null;
    }

    private string? KnownSkill(ProtoId<CFAbilitySkillPrototype>? skill)
    {
        return skill != null && _prototypes.HasIndex(skill.Value) ? skill.Value.Id : null;
    }

    private void ApplyAncestry(EntityUid mob, CFAncestryPrototype? ancestry, CFHeritagePrototype? heritage, int speedBonus)
    {
        var speed = CFAttributeMath.EffectiveSpeed(ancestry, heritage) + Math.Max(0, speedBonus);
        if (speed > 0 && TryComp(mob, out CFCombatantComponent? combat))
        {
            combat.SpeedFeet = speed;
            Dirty(mob, combat);
        }

        if (speed > 0 && speed != (int) BaselineSpeedFeet)
        {
            var scale = speed / BaselineSpeedFeet;
            // 2.5 walk, 4.5 sprint, and 20 acceleration are the usual humanoid speeds.
            _speed.ChangeBaseSpeed(mob, 2.5f * scale, 4.5f * scale, 20f);
        }

        if (!CFAttributeMath.HasDarkvision(ancestry, heritage))
            return;

        _features.GrantSense(mob, CFSense.Darkvision);
    }

    /// <summary>
    /// Known spells become radial actions when a matching <c>CF{spell}</c> entity exists.
    /// </summary>
    private void GrantSpellActions(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile)
    {
        foreach (var pick in profile.CFBuild.Spells)
        {
            if (pick.HasSpell && pick.Rank >= 0)
                _features.GrantSpell(mob, pick.Spell);
        }
    }

    /// <summary>
    /// Copies class spell slots onto the mob so the HUD can draw pips.
    /// Expended slots come from the character sheet. Cantrips stay in index 0 and are not drawn.
    /// </summary>
    private void ApplySpellbook(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile, CFClassPrototype? cls)
    {
        var casting = cls?.Spellcasting;
        if (casting == null)
            return;

        var book = EnsureComp<CFSpellbookComponent>(mob);
        book.Slots.Clear();
        book.Slots.Add(casting.Cantrips);
        book.Slots.AddRange(casting.Slots);

        book.Expended.Clear();
        for (var i = 0; i < book.Slots.Count; i++)
            book.Expended.Add(0);

        foreach (var pick in profile.CFBuild.Spells)
        {
            if (!pick.Expended || !pick.HasSpell || pick.Rank < 0 || pick.Rank >= book.Expended.Count)
                continue;

            book.Expended[pick.Rank]++;
        }

        book.FocusPoints = casting.FocusPoints;
        book.FocusSpent = 0;
        if (cls != null && cls.ID == "Cleric" && book.Slots.Count > 1 && TryComp(mob, out CFAbilityScoreComponent? scores))
            book.Slots[1] += Math.Max(1, scores.GetModifier(CFAbility.Charisma));

        Dirty(mob, book);
    }

    /// <summary>
    /// What every character has, then the grants of everything chosen for level 1.
    /// Later feats are stored on <see cref="CFExperienceComponent"/> and granted on level up.
    /// </summary>
    private void ApplyFeatures(
        EntityUid mob,
        Content.Shared.Preferences.HumanoidCharacterProfile profile,
        CFAncestryPrototype? ancestry,
        CFHeritagePrototype? heritage,
        CFClassPrototype? cls,
        List<CFFeatPrototype> feats)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        EnsureComp<CFGOAPScentEmitterComponent>(mob);
        _features.GrantAction(mob, "CFActLeap");

        if (ancestry != null)
            _features.Apply(mob, ancestry.Grants, $"ancestry:{ancestry.ID}");

        if (heritage != null)
            _features.Apply(mob, heritage.Grants, $"heritage:{heritage.ID}");

        if (_prototypes.Resolve(profile.CFBuild.Background, out var background))
            _features.Apply(mob, background.Grants, $"background:{background.ID}");

        if (cls != null)
            _features.ApplyClassFeatures(mob, cls, 1);

        foreach (var choice in CFClassChoices.Selected(_prototypes, profile.CFBuild))
            _features.ApplyClassChoice(mob, choice, 1);

        if (CFClassChoices.SelectedDeity(_prototypes, profile.CFBuild) is { } deity)
            _features.Apply(mob, deity.Grants, $"deity:{deity.ID}");

        foreach (var feat in feats)
            ApplyFeat(mob, feat, options, adjustSpeed: false);

        Dirty(mob, options);

        // A grant can raise an armor proficiency, so AC is worked out again now they have all run.
        _armor.RefreshArmorClass(mob);
    }

    /// <summary>Trains the feat's skills, replaces the fist when it should, and runs the feat's grants.</summary>
    public void ApplyFeat(EntityUid mob, CFFeatPrototype feat, CFCombatOptionsComponent? options = null, bool adjustSpeed = true)
    {
        options ??= EnsureComp<CFCombatOptionsComponent>(mob);
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
        _features.Apply(mob, feat.Grants, $"feat:{feat.ID}");
    }

    /// <summary>Armor class follows the new level. Hit points are changed by the level-up itself.</summary>
    public void RefreshArmorClass(EntityUid mob)
    {
        _armor.RefreshArmorClass(mob);
    }

    private static string? IdOf(ProtoId<CFFeatPrototype>? feat)
    {
        return feat?.Id;
    }
}
