using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Companions;

/// <summary>
/// Young companion and familiar numbers for the pet tab.
/// Companion math follows the animal companion rules: trained proficiency, item bonuses only to AC and Speed, and a +3 cap on the AC item bonus.
/// </summary>
public static class CFPetMath
{
    public const int Trained = 2;
    public const int MaxArmorItemBonus = 3;

    public static bool ClassHasCompanion(IPrototypeManager prototypes, string? classId)
    {
        return ClassOffers(prototypes, classId, companion: true);
    }

    public static bool ClassHasFamiliar(IPrototypeManager prototypes, string? classId)
    {
        return ClassOffers(prototypes, classId, companion: false);
    }

    /// <summary>The saved build actually has this companion or familiar, not only the option to take one.</summary>
    /// <param name="atLevel">The character's level, when it is not the build's own: a level taken in play.</param>
    public static bool BuildGrants(IPrototypeManager prototypes, CFCharacterBuildData build, CFPetKind kind, int atLevel = 0)
    {
        if (kind == CFPetKind.Construct && build.ClassChoices.Contains("InnovationConstruct"))
            return true;

        var level = Math.Max(1, Math.Max(atLevel, build.Level));

        // A feat that another option hands out, such as the Leaf order's Leshy Familiar, counts as much as a picked one.
        foreach (var extra in CFSkills.ExtraFeats(prototypes, build, null, level))
        {
            if (Gives(extra, kind))
                return true;
        }

        if (kind != CFPetKind.Construct && build.Class != null && prototypes.TryIndex(build.Class.Value, out CFClassPrototype? cls))
        {
            foreach (var feature in cls.Features)
            {
                if (feature.Level <= level && Matches(feature.Name, kind == CFPetKind.Companion))
                    return true;
            }
        }

        // A feat counts from the level of the slot it sits in, not from its own level.
        return FeatGrants(prototypes, build.ClassFeat, kind)
               || FeatGrants(prototypes, build.AncestryFeat, kind)
               || level >= 2 && FeatGrants(prototypes, build.ClassFeatLevel2, kind)
               || level >= 4 && FeatGrants(prototypes, build.ClassFeatLevel4, kind)
               || level >= 5 && FeatGrants(prototypes, build.AncestryFeatLevel5, kind);
    }

    public static CFPetBlock Present(
        CFPetPrototype pet,
        CFCharacterBuildData build,
        CFCharacterSheet? master)
    {
        var level = Math.Max(1, build.Level);
        var barding = pet.Kind == CFPetKind.Companion ? BardingOf(build.PetBarding) : BardingOf("");

        // An advanced construct: Strength, Dexterity, Constitution and Wisdom one higher, two damage dice, expert saves.
        var advanced = IsAdvancedConstruct(pet, build);
        var step = advanced ? 1 : 0;
        var saveRank = advanced ? Expert : Trained;
        var strength = pet.Strength + step;
        var dexterity = pet.Dexterity + step;
        var constitution = pet.Constitution + step;
        var wisdom = pet.Wisdom + step;

        var dexCap = Math.Min(dexterity, barding.DexCap);
        var item = Math.Min(MaxArmorItemBonus, Math.Max(0, barding.Bonus));
        var armor = 10 + level + dexCap + Trained + item;
        var fort = level + constitution + saveRank;
        var reflex = level + dexterity + saveRank;
        var will = level + wisdom + saveRank;
        var perception = level + wisdom + saveRank;
        if (pet.UsesMaster && master != null)
        {
            armor = master.ArmorClass + barding.Bonus;
            fort = master.Fortitude;
            reflex = master.Reflex;
            will = master.Will;
            var trained = MasterTrainedPerception(master.Class);
            var masterWisdom = master.Modifiers.TryGetValue(CFAbility.Wisdom, out var wis) ? wis : 0;
            perception = master.Level + masterWisdom + (trained ? Trained : 0);
        }

        var hp = pet.ScaleHitPoints ? pet.HitPoints + (6 + constitution) * level : pet.HitPoints;

        var strikes = new List<CFPetStrike>();
        foreach (var attack in pet.Attacks)
        {
            var ability = attack.Finesse ? Math.Max(strength, dexterity) : strength;
            var tags = new List<string>();
            if (attack.Agile)
                tags.Add("Agile");
            if (attack.Finesse)
                tags.Add("Finesse");
            foreach (var trait in attack.Traits)
            {
                if (string.IsNullOrWhiteSpace(trait))
                    continue;
                var label = trait.Substring(0, 1).ToUpperInvariant();
                if (trait.Length > 1)
                    label = string.Concat(label, trait.Substring(1));
                tags.Add(label);
            }

            var damageBonus = strength;
            var bonus = damageBonus > 0 ? "+" + damageBonus : damageBonus < 0 ? damageBonus.ToString() : "";
            var dice = advanced ? Math.Max(2, attack.Dice) : attack.Dice;
            strikes.Add(new CFPetStrike(
                attack.Name,
                level + ability + Trained,
                $"{dice}d{attack.Die}{bonus} {attack.Damage}",
                tags));
        }

        var skills = new List<CFPetSkillLine>();
        if (pet.Kind is CFPetKind.Companion or CFPetKind.Construct)
        {
            AddTrainedSkill(skills, pet, level, "Acrobatics", CFAbility.Dexterity, step);
            AddTrainedSkill(skills, pet, level, "Athletics", CFAbility.Strength, step);
        }

        if (advanced)
        {
            AddTrainedSkill(skills, pet, level, "Intimidation", CFAbility.Charisma, step);
            AddTrainedSkill(skills, pet, level, "Stealth", CFAbility.Dexterity, step);
            AddTrainedSkill(skills, pet, level, "Survival", CFAbility.Wisdom, step);
        }

        foreach (var skill in pet.Skills)
            AddTrainedSkill(skills, pet, level, skill.Name, skill.Ability, step);

        var speed = new List<string>();
        foreach (var entry in pet.Speeds)
        {
            var feet = entry.Feet;
            if (barding.SpeedPenalty > 0 && entry.Name == "Speed")
                feet = Math.Max(5, feet - barding.SpeedPenalty);
            speed.Add(entry.Name == "Speed" ? $"{feet} feet" : $"{entry.Name} {feet} feet");
        }

        return new CFPetBlock(
            string.IsNullOrWhiteSpace(build.PetName) || build.PetId != pet.ID ? pet.Name : build.PetName,
            pet,
            level,
            armor,
            hp,
            barding.Label,
            strikes,
            fort,
            reflex,
            will,
            perception,
            speed.Count == 0 ? "—" : string.Join(", ", speed),
            pet.Mount ? pet.Size + " (mount)" : pet.Size,
            pet.Senses.Count == 0 ? "—" : string.Join(", ", pet.Senses),
            pet.Support,
            CompanionAdvanced(pet),
            skills);
    }

    /// <summary>Ancestry Hit Points, plus 6 + Constitution for each level the master has.</summary>
    public static int YoungHitPoints(CFPetPrototype pet, int level)
    {
        return pet.HitPoints + (6 + pet.Constitution) * Math.Max(1, level);
    }

    /// <summary>A construct is advanced once its Inventor has Advanced Construct Companion, a level 4 feat, at level 4 or above.</summary>
    public static bool IsAdvancedConstruct(CFPetPrototype pet, CFCharacterBuildData build)
    {
        return pet.Kind == CFPetKind.Construct && build.Level >= 4 && build.ClassFeatLevel4?.Id == AdvancedConstructFeat;
    }

    public const string AdvancedConstructFeat = "CFAdvancedConstructCompanion";
    public const int Expert = 4;

    public static CFPetBarding BardingOf(string id)
    {
        return id switch
        {
            "light" => new CFPetBarding("light", "Light barding", 1, 5, 0),
            "heavy" => new CFPetBarding("heavy", "Heavy barding", 2, 3, 5),
            _ => new CFPetBarding("", "Unarmored", 0, 99, 0),
        };
    }

    private static string CompanionAdvanced(CFPetPrototype pet)
    {
        if (pet.Kind != CFPetKind.Companion || string.IsNullOrWhiteSpace(pet.Advanced))
            return pet.Advanced;

        return pet.Advanced + ". A young companion learns this when it becomes nimble or savage.";
    }

    private static void AddTrainedSkill(
        List<CFPetSkillLine> skills,
        CFPetPrototype pet,
        int level,
        string name,
        CFAbility ability,
        int step = 0)
    {
        foreach (var existing in skills)
        {
            if (existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var mod = ability switch
        {
            CFAbility.Strength => pet.Strength + step,
            CFAbility.Dexterity => pet.Dexterity + step,
            CFAbility.Constitution => pet.Constitution + step,
            CFAbility.Intelligence => pet.Intelligence,
            CFAbility.Wisdom => pet.Wisdom + step,
            _ => pet.Charisma,
        };
        skills.Add(new CFPetSkillLine(name, level + mod + Trained));
    }

    private static bool ClassOffers(IPrototypeManager prototypes, string? classId, bool companion)
    {
        if (string.IsNullOrEmpty(classId) || !prototypes.TryIndex<CFClassPrototype>(classId, out var cls))
            return false;

        foreach (var feature in cls.Features)
        {
            if (Matches(feature.Name, companion))
                return true;
        }

        foreach (var feat in prototypes.EnumeratePrototypes<CFFeatPrototype>())
        {
            if (feat.Level > 5)
                continue;
            if (!string.Equals(feat.Class, classId, StringComparison.Ordinal)
                && !feat.Classes.Contains(classId))
                continue;
            if (Gives(feat, companion ? CFPetKind.Companion : CFPetKind.Familiar))
                return true;
        }

        return false;
    }

    private static bool FeatGrants(IPrototypeManager prototypes, ProtoId<CFFeatPrototype>? id, CFPetKind kind)
    {
        return id != null && prototypes.TryIndex(id.Value, out CFFeatPrototype? feat) && Gives(feat, kind);
    }

    /// <summary>The feat has a <see cref="CFGrantPet"/> of this kind.</summary>
    private static bool Gives(CFFeatPrototype feat, CFPetKind kind)
    {
        foreach (var grant in feat.Grants)
        {
            if (grant is CFGrantPet pet && pet.Kind == kind)
                return true;
        }

        return false;
    }

    /// <summary>Takes a class feature's name, so spaces do not count. Feats say it with a <see cref="CFGrantPet"/>.</summary>
    private static bool Matches(string name, bool companion)
    {
        name = name.Replace(" ", string.Empty);
        if (companion)
            return name.Contains("AnimalCompanion", StringComparison.OrdinalIgnoreCase);

        return name.Contains("Familiar", StringComparison.OrdinalIgnoreCase)
               && !name.Contains("Familiarity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MasterTrainedPerception(CFClassPrototype? cls)
    {
        if (cls == null)
            return false;

        return cls.Proficiencies.Perception.Trained.Contains("Perception")
               || cls.Proficiencies.Perception.Expert.Contains("Perception")
               || cls.Proficiencies.Perception.Master.Contains("Perception")
               || cls.Proficiencies.Perception.Legendary.Contains("Perception");
    }
}

public readonly record struct CFPetBarding(string Id, string Label, int Bonus, int DexCap, int SpeedPenalty);

public sealed class CFPetStrike
{
    public CFPetStrike(string name, int attack, string damage, List<string> traits)
    {
        Name = name;
        Attack = attack;
        Damage = damage;
        Traits = traits;
    }

    public string Name { get; }
    public int Attack { get; }
    public string Damage { get; }
    public List<string> Traits { get; }
}

public sealed class CFPetSkillLine
{
    public CFPetSkillLine(string name, int modifier)
    {
        Name = name;
        Modifier = modifier;
    }

    public string Name { get; }
    public int Modifier { get; }
}

public sealed class CFPetBlock
{
    public CFPetBlock(
        string title,
        CFPetPrototype pet,
        int level,
        int armorClass,
        int hitPoints,
        string armor,
        List<CFPetStrike> strikes,
        int fortitude,
        int reflex,
        int will,
        int perception,
        string speed,
        string size,
        string senses,
        string support,
        string advanced,
        List<CFPetSkillLine> skills)
    {
        Title = title;
        Pet = pet;
        Level = level;
        ArmorClass = armorClass;
        HitPoints = hitPoints;
        Armor = armor;
        Strikes = strikes;
        Fortitude = fortitude;
        Reflex = reflex;
        Will = will;
        Perception = perception;
        Speed = speed;
        Size = size;
        Senses = senses;
        Support = support;
        Advanced = advanced;
        Skills = skills;
    }

    public string Title { get; }
    public CFPetPrototype Pet { get; }
    public int Level { get; }
    public int ArmorClass { get; }
    public int HitPoints { get; }
    public string Armor { get; }
    public List<CFPetStrike> Strikes { get; }
    public int Fortitude { get; }
    public int Reflex { get; }
    public int Will { get; }
    public int Perception { get; }
    public string Speed { get; }
    public string Size { get; }
    public string Senses { get; }
    public string Support { get; }
    public string Advanced { get; }
    public List<CFPetSkillLine> Skills { get; }
}
