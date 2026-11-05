using Content.Server.CF.NPC;
using System.Numerics;
using Content.Shared.Actions;
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
using Content.Shared.Overlays;
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
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private CFActionPoolSystem _actionPool = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFCompanionSystem _companions = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private CFArmorSystem _armor = default!;

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
        _actionPool.Ensure(mob);
        ApplySpellbook(mob, profile, sheet.Class);
        GrantSpellActions(mob, profile);
        GrantActivities(mob, profile, sheet.Class, chosenFeats);
    }

    /// <summary>
    /// Speed scales the usual walk and sprint. Darkvision uses the same night-vision overlay as goblins.
    /// </summary>
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

        var vision = EnsureComp<NightVisionComponent>(mob);
        vision.Enabled = true;
        vision.Prioritized = true;
        vision.LightingColor = new Color((byte) 0x9B, (byte) 0xB6, (byte) 0xFF, (byte) 0x30);
        vision.NoiseAmount = 0f;
        Dirty(mob, vision);
    }

    /// <summary>
    /// Known spells become radial actions when a matching <c>CF{spell}</c> entity exists.
    /// </summary>
    private void GrantSpellActions(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile)
    {
        var granted = new HashSet<string>();
        foreach (var pick in profile.CFBuild.Spells)
        {
            if (!pick.HasSpell || pick.Rank < 0)
                continue;

            var id = "CF" + pick.Spell.Id;
            if (!granted.Add(id) || !_prototypes.TryIndex<EntityPrototype>(id, out _))
                continue;

            _actions.AddAction(mob, id);
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
    /// Basic skill actions, class identity, and the level 1 feats already chosen.
    /// Later feats are stored on <see cref="CFExperienceComponent"/> and granted on level up.
    /// </summary>
    private void GrantActivities(
        EntityUid mob,
        Content.Shared.Preferences.HumanoidCharacterProfile profile,
        CFClassPrototype? cls,
        List<CFFeatPrototype> feats)
    {
        var options = EnsureComp<CFCombatOptionsComponent>(mob);
        EnsureComp<CFScentEmitterComponent>(mob);
        Grant(mob, "CFActLeap");

        if (_prototypes.Resolve(profile.CFBuild.Background, out var background) && background.BattleMedicine)
            options.BattleMedicine = true;

        switch (cls?.ID)
        {
            case "Rogue":
                options.SneakAttack = true;
                break;
            case "Barbarian":
                options.CanRage = true;
                Grant(mob, "CFActRage");
                break;
            case "Fighter":
                options.ReactiveStrike = true;
                options.ShieldBlock = true;
                break;
            case "Monk":
                options.Flurry = true;
                options.PowerfulFist = true;
                Grant(mob, "CFActFlurry");
                break;
            case "Ranger":
                options.PointMark = CFPointMark.Prey;
                break;
            case "Swashbuckler":
                options.PanacheStyle = true;
                break;
            case "Magus":
                options.Spellstrike = true;
                Grant(mob, "CFActSpellstrike");
                break;
            case "Investigator":
                options.Devise = true;
                options.PointMark = CFPointMark.Stratagem;
                break;
            case "Thaumaturge":
                options.Exploit = true;
                options.PointMark = CFPointMark.Exploit;
                break;
            case "Bard":
                options.Anthem = true;
                Grant(mob, "CFActAnthem");
                break;
            case "Champion":
                options.Champion = true;
                options.ShieldBlock = true;
                break;
            case "Alchemist":
                Spawn("CFHealingPotionMinor", Transform(mob).Coordinates);
                Spawn("CFHealingPotionMinor", Transform(mob).Coordinates);
                break;
        }

        foreach (var feat in feats)
            ApplyFeat(mob, feat, options, adjustSpeed: false);

        Dirty(mob, options);
    }

    /// <summary>Trains the feat's skills, replaces the fist when it should, and grants a feat action that already runs.</summary>
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

        switch (feat.ID)
        {
            case "CFDoubleSlice":
                options.DoubleSlice = true;
                break;
            case "CFExactingStrike":
                options.ExactingStrike = true;
                break;
            case "CFSuddenChargeFighter":
            case "CFSuddenChargeBarbarian":
                Grant(mob, "CFActSuddenCharge");
                break;
            case "CFSnaggingStrike":
                options.SnaggingStrike = true;
                break;
            case "CFViciousSwing":
                options.ViciousSwing = true;
                break;
            case "CFPointBlankStance":
                Grant(mob, "CFActPointBlank");
                break;
            case "CFReactiveShieldFighter":
            case "CFReactiveShieldGuardian":
                options.ReactiveShield = true;
                break;
            case "CFTwinFeint":
                options.TwinFeint = true;
                break;
            case "CFYoureNextRogue":
            case "CFYoureNextSwashbuckler":
                options.YoureNext = true;
                break;
            case "CFNimbleDodge":
                options.NimbleDodge = true;
                break;
            case "CFTumbleBehindRogue":
            case "CFTumbleBehindSwashbuckler":
                options.TumbleBehind = true;
                break;
        }

        Dirty(mob, combat);
        Dirty(mob, options);
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

    private void Grant(EntityUid mob, string id)
    {
        if (_prototypes.TryIndex<EntityPrototype>(id, out _))
            _actions.AddAction(mob, id);
    }
}
