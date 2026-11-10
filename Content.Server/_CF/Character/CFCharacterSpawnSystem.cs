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
    /// <summary>The HUD deck's Common tab. Everyone has these.</summary>
    private static readonly EntProtoId[] BasicActions =
    {
        "CFActManeuver",
        "CFActGrapple",
        "CFActTumble",
        "CFActDemoralize",
        "CFActFeint",
        "CFActAid",
        "CFActRecall",
        "CFActHide",
        "CFActSeek",
    };

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFActionPoolSystem _actionPool = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFCompanionSystem _companions = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;
    [Dependency] private Content.Server.CF.Shop.CFShopSystem _shop = default!;

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
        else if (CFPetMath.BuildGrants(_prototypes, build, CFPetKind.Construct))
        {
            pet = FirstPet(CFPetKind.Construct);
        }

        if (pet == null)
            return;

        var prototype = _prototypes.HasIndex<EntityPrototype>(pet.Entity) ? pet.Entity : "CFSummon";
        var companion = Spawn(prototype, Transform(mob).Coordinates.Offset(new Vector2(1f, 0f)));
        _companions.Bind(mob, companion);
        if (TryComp(companion, out CFCompanionComponent? bound))
        {
            bound.PetId = pet.ID;
            bound.Barding = build.PetBarding;
        }

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
        var copies = CFCoins.AffordableCopies(profile, jobId, _prototypes, out var left);
        foreach (var (item, count) in copies)
        {
            for (var i = 0; i < count; i++)
            {
                _shop.GiveOne(mob, item);
            }
        }

        var purse = EnsureComp<CFPurseComponent>(mob);
        purse.Copper = left;
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
        // The build comes from a client, so a feat it cannot have goes before anything reads it.
        var checkedBuild = profile.CFBuild.Clone();
        CFSkills.DropUnqualifiedFeats(_prototypes, checkedBuild, ancestryId);
        var chosenFeats = CFSkills.SelectedFeats(_prototypes, checkedBuild, ancestryId, classId, 1);
        var speedBonus = 0;
        combatant.TrainedSkills = CFSkills.CollectTrainedSkills(_prototypes, checkedBuild, ancestryId, classId, 1);
        var xp = EnsureComp<CFExperienceComponent>(mob);
        xp.Xp = 0;
        xp.AncestryFeat = IdOf(profile.CFBuild.AncestryFeat);
        xp.ClassFeat = IdOf(profile.CFBuild.ClassFeat);
        xp.ClassFeatLevel2 = ChoiceFeat(profile.CFBuild, profile.CFBuild.ClassFeatLevel2);
        xp.ClassFeatLevel4 = ChoiceFeat(profile.CFBuild, profile.CFBuild.ClassFeatLevel4);
        xp.AncestryFeatLevel5 = IdOf(profile.CFBuild.AncestryFeatLevel5);
        xp.SkillFeatLevel2 = QualifiedFeat(checkedBuild, ancestryId, checkedBuild.SkillFeatLevel2, 2, skillSlot: true);
        xp.GeneralFeatLevel3 = QualifiedFeat(checkedBuild, ancestryId, checkedBuild.GeneralFeatLevel3, 3, skillSlot: false);
        xp.SkillFeatLevel4 = QualifiedFeat(checkedBuild, ancestryId, checkedBuild.SkillFeatLevel4, 4, skillSlot: true);
        xp.SkillIncreaseLevel3 = KnownSkill(checkedBuild.SkillIncreaseLevel3);
        xp.SkillIncreaseLevel5 = KnownSkill(checkedBuild.SkillIncreaseLevel5);
        xp.BonusSkillFeats = checkedBuild.BonusSkillFeats
            .Where(pair => pair.Key > 1 && QualifiedFeat(checkedBuild, ancestryId, new ProtoId<CFFeatPrototype>(pair.Value), pair.Key, skillSlot: true) != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        xp.BonusSkillIncreases = checkedBuild.BonusSkillIncreases
            .Where(pair => _prototypes.HasIndex<CFAbilitySkillPrototype>(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        xp.AbilityBoostsLevel5 = profile.CFBuild.AbilityBoostsLevel5.Distinct().Take(CFAttributeMath.LevelFiveBoostCount).ToList();
        xp.ClassChoices = CFClassChoices.Selected(_prototypes, profile.CFBuild).Select(choice => choice.ID).ToList();
        xp.OptionPicks = CFOptionChoices.Valid(_prototypes, checkedBuild, ancestryId);
        xp.SpellPicks = profile.CFBuild.Spells.Select(spell => spell.Copy()).ToList();
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
        xp.LanguageLevel5 = CFLanguageRules.LevelFiveLanguage(sheet.Modifiers, xp.AbilityBoostsLevel5)
            ? CFLanguageRules.LevelFivePick(_prototypes, sheet.Ancestry, profile.CFBuild.Languages, sheet.Modifiers)?.Id
            : null;
        Dirty(mob, languages);
        _actionPool.Ensure(mob);
        ApplySpellbook(mob, profile, sheet.Class);
        GrantSpellActions(mob, profile, sheet.Class);
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
    private void GrantSpellActions(EntityUid mob, Content.Shared.Preferences.HumanoidCharacterProfile profile, CFClassPrototype? cls)
    {
        if (cls?.Spellcasting is { } casting)
        {
            // Picks for later levels wait for GrowSpellbook. A spell with no slot of its rank would otherwise be at will.
            foreach (var pick in CFSpellSlots.Within(profile.CFBuild.Spells, casting.AllSlotsAt(1), _prototypes))
            {
                _features.GrantSpell(mob, pick.Spell);
            }

            return;
        }

        foreach (var pick in profile.CFBuild.Spells)
        {
            if (pick.HasSpell && pick.Rank >= 0)
                _features.GrantSpell(mob, pick.Spell);
        }
    }

    /// <summary>
    /// Gives a caster its new level's slots, and the spells picked for them in the lobby. A prepared caster has them ready at once,
    /// as well as planned for its next long rest.
    /// </summary>
    public void GrowSpellbook(EntityUid mob, CFClassPrototype cls, int level, CFExperienceComponent xp)
    {
        if (cls.Spellcasting is not { } casting || !TryComp(mob, out CFSpellbookComponent? book))
            return;

        var before = casting.AllSlotsAt(level - 1);
        var after = casting.AllSlotsAt(level);
        var known = CFSpellSlots.Within(xp.SpellPicks, before, _prototypes);
        foreach (var pick in CFSpellSlots.Within(xp.SpellPicks, after, _prototypes))
        {
            if (known.Exists(old => old.Rank == pick.Rank && old.Index == pick.Index))
                continue;

            _features.GrantSpell(mob, pick.Spell);
            if (pick.Rank <= 0)
                continue;

            if (!casting.Prepared)
            {
                book.KnownAtRank.Add(new CFPreparedSlot(pick.Spell, pick.Rank));
                continue;
            }

            if (!book.Repertoire.Contains(pick.Spell))
                book.Repertoire.Add(pick.Spell);

            book.PreparedSlots.Add(new CFPreparedSlot(pick.Spell, pick.Rank));
            book.PlannedSlots.Add(new CFPreparedSlot(pick.Spell, pick.Rank));
        }

        // Divine Font's slots move up to the new highest rank, so Heal and Harm heighten with the cleric.
        book.Slots = new List<int>(after);
        AddFontSlots(book);

        book.PreparableSlots = new List<int>(after);
        while (book.Expended.Count < book.Slots.Count)
        {
            book.Expended.Add(0);
        }

        Dirty(mob, book);
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

        ApplyPrepared(book, profile, casting);

        book.FocusPoints = casting.FocusPoints;
        book.FocusSpent = 0;
        if (cls != null && cls.ID == "Cleric" && book.Slots.Count > 1 && TryComp(mob, out CFAbilityScoreComponent? scores))
        {
            // Divine Font: extra slots of the highest rank, for what the deity offers.
            book.FontSlots = Math.Max(1, scores.GetModifier(CFAbility.Charisma));
            AddFontSlots(book);
            if (CFClassChoices.SelectedDeity(_prototypes, profile.CFBuild) is { DivineFont.Count: > 0 } deity)
            {
                book.SpareSlotSpells.AddRange(deity.DivineFont);
            }
            else
            {
                book.SpareSlotSpells.Add("Heal");
                book.SpareSlotSpells.Add("Harm");
            }
        }

        Dirty(mob, book);
    }

    private static void AddFontSlots(CFSpellbookComponent book)
    {
        if (book.FontSlots <= 0)
            return;

        for (var rank = book.Slots.Count - 1; rank > 0; rank--)
        {
            if (book.Slots[rank] <= 0)
                continue;

            book.Slots[rank] += book.FontSlots;
            return;
        }
    }

    /// <summary>A prepared caster starts with one charge per spell picked in the lobby, and plans the same for its next long rest.</summary>
    private void ApplyPrepared(
        CFSpellbookComponent book,
        Content.Shared.Preferences.HumanoidCharacterProfile profile,
        CFSpellcasting casting)
    {
        book.Prepared = casting.Prepared;
        book.PreparedSlots.Clear();
        book.PlannedSlots.Clear();
        book.Repertoire.Clear();
        book.SpareSlotSpells.Clear();
        book.KnownAtRank.Clear();
        book.SignatureSpells.Clear();
        book.PreparableSlots = new List<int>(book.Slots);
        foreach (var pick in CFSpellSlots.Within(profile.CFBuild.Spells, book.Slots, _prototypes))
        {
            if (pick.Rank <= 0)
                continue;

            if (!casting.Prepared)
            {
                book.KnownAtRank.Add(new CFPreparedSlot(pick.Spell, pick.Rank));
                continue;
            }

            if (!book.Repertoire.Contains(pick.Spell))
                book.Repertoire.Add(pick.Spell);

            book.PreparedSlots.Add(new CFPreparedSlot(pick.Spell, pick.Rank, pick.Expended));
            book.PlannedSlots.Add(new CFPreparedSlot(pick.Spell, pick.Rank));
        }
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
        foreach (var action in BasicActions)
            _features.GrantAction(mob, action);

        if (ancestry != null)
            _features.Apply(mob, ancestry.Grants, $"ancestry:{ancestry.ID}");

        if (heritage != null)
        {
            _features.Apply(mob, heritage.Grants, $"heritage:{heritage.ID}");
            _features.ApplyOptionPicks(mob, heritage.ID);
        }

        if (_prototypes.Resolve(profile.CFBuild.Background, out var background))
            _features.Apply(mob, background.Grants, $"background:{background.ID}");

        if (cls != null)
            _features.ApplyClassFeatures(mob, cls, 1);

        foreach (var choice in CFClassChoices.Selected(_prototypes, profile.CFBuild))
        {
            _features.ApplyClassChoice(mob, choice, 1);
            _features.ApplyOptionPicks(mob, choice.ID);
        }

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
        _features.ApplyOptionPicks(mob, feat.ID);
    }

    /// <summary>Armor class follows the new level. Hit points are changed by the level-up itself.</summary>
    public void RefreshArmorClass(EntityUid mob)
    {
        _armor.RefreshArmorClass(mob);
    }

    /// <summary>The feat, if the build has the subclass option it asks for.</summary>
    private string? ChoiceFeat(CFCharacterBuildData build, ProtoId<CFFeatPrototype>? id)
    {
        return id is { } feat && _prototypes.TryIndex(feat, out CFFeatPrototype? proto) && !CFClassChoices.MeetsRequirement(_prototypes, build, proto)
            ? null
            : id?.Id;
    }

    private static string? IdOf(ProtoId<CFFeatPrototype>? feat)
    {
        return feat?.Id;
    }
}
