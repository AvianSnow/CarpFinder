using Content.Shared.CF.Character;
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
    public static bool BuildGrants(IPrototypeManager prototypes, CFCharacterBuildData build, CFPetKind kind)
    {
        var companion = kind == CFPetKind.Companion;
        var level = Math.Max(1, build.Level);
        if (build.Class != null && prototypes.TryIndex(build.Class.Value, out CFClassPrototype? cls))
        {
            foreach (var feature in cls.Features)
            {
                if (feature.Level <= level && Matches(feature.Name, companion))
                    return true;
            }
        }

        return FeatGrants(prototypes, build.ClassFeat, companion, level)
               || FeatGrants(prototypes, build.AncestryFeat, companion, level)
               || FeatGrants(prototypes, build.ClassFeatLevel2, companion, level)
               || FeatGrants(prototypes, build.ClassFeatLevel4, companion, level)
               || FeatGrants(prototypes, build.AncestryFeatLevel5, companion, level);
    }

    public static CFPetBlock Present(
        CFPetPrototype pet,
        CFCharacterBuildData build,
        CFCharacterSheet? master)
    {
        var level = Math.Max(1, build.Level);
        var barding = pet.Kind == CFPetKind.Companion ? BardingOf(build.PetBarding) : BardingOf("");
        var dexCap = Math.Min(pet.Dexterity, barding.DexCap);
        var item = Math.Min(MaxArmorItemBonus, Math.Max(0, barding.Bonus));
        var armor = 10 + level + dexCap + Trained + item;
        var fort = level + pet.Constitution + Trained;
        var reflex = level + pet.Dexterity + Trained;
        var will = level + pet.Wisdom + Trained;
        var perception = level + pet.Wisdom + Trained;
        if (pet.UsesMaster && master != null)
        {
            armor = master.ArmorClass + barding.Bonus;
            fort = master.Fortitude;
            reflex = master.Reflex;
            will = master.Will;
            var trained = MasterTrainedPerception(master.Class);
            var wisdom = master.Modifiers.TryGetValue(CFAbility.Wisdom, out var wis) ? wis : 0;
            perception = master.Level + wisdom + (trained ? Trained : 0);
        }

        var hp = pet.ScaleHitPoints ? YoungHitPoints(pet, level) : pet.HitPoints;

        var strikes = new List<CFPetStrike>();
        foreach (var attack in pet.Attacks)
        {
            var ability = attack.Finesse ? Math.Max(pet.Strength, pet.Dexterity) : pet.Strength;
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

            var damageBonus = pet.Strength;
            var bonus = damageBonus > 0 ? "+" + damageBonus : damageBonus < 0 ? damageBonus.ToString() : "";
            strikes.Add(new CFPetStrike(
                attack.Name,
                level + ability + Trained,
                $"{attack.Dice}d{attack.Die}{bonus} {attack.Damage}",
                tags));
        }

        var skills = new List<CFPetSkillLine>();
        if (pet.Kind == CFPetKind.Companion)
        {
            AddTrainedSkill(skills, pet, level, "Acrobatics", CFAbility.Dexterity);
            AddTrainedSkill(skills, pet, level, "Athletics", CFAbility.Strength);
        }

        foreach (var skill in pet.Skills)
            AddTrainedSkill(skills, pet, level, skill.Name, skill.Ability);

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
        CFAbility ability)
    {
        foreach (var existing in skills)
        {
            if (existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var mod = ability switch
        {
            CFAbility.Strength => pet.Strength,
            CFAbility.Dexterity => pet.Dexterity,
            CFAbility.Constitution => pet.Constitution,
            CFAbility.Intelligence => pet.Intelligence,
            CFAbility.Wisdom => pet.Wisdom,
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
            if (Matches(feat.ID, companion))
                return true;
        }

        return false;
    }

    private static bool FeatGrants(IPrototypeManager prototypes, ProtoId<CFFeatPrototype>? id, bool companion, int level)
    {
        if (id == null || !prototypes.TryIndex(id.Value, out CFFeatPrototype? feat))
            return false;

        return feat.Level <= level && Matches(feat.ID, companion);
    }

    /// <summary>Takes a class feature's name or a feat's id, so spaces do not count.</summary>
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
