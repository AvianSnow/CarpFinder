using System.Linq;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Character;

/// <summary>A choice a character makes on reaching a level.</summary>
[Serializable, NetSerializable]
public enum CFLevelSlot : byte
{
    ClassFeat,
    AncestryFeat,
    SkillFeat,
    GeneralFeat,
    BonusSkillFeat,
    SkillIncrease,
    BonusSkillIncrease,
    Boosts,
    Language,
}

/// <summary>One spell slot a level adds: its rank, and its place among that rank's slots.</summary>
public readonly record struct CFNewSpellSlot(int Rank, int Index);

/// <summary>
/// The player picks their next level in game, on the character sheet. The lobby only builds level 1. This holds what
/// each level asks for and what each pick may be, so the sheet and the server read the same rules. The picks live on
/// the character's <see cref="CFCharacterBuildData"/>, in the same fields the lobby used to fill.
/// </summary>
public static class CFLevelUp
{
    /// <summary>What a character picks on reaching <paramref name="level"/>.</summary>
    public static List<CFLevelSlot> SlotsAt(IPrototypeManager prototypes, CFCharacterBuildData build, int level)
    {
        var slots = new List<CFLevelSlot>();
        switch (level)
        {
            case 2:
            case 4:
                slots.Add(CFLevelSlot.ClassFeat);
                slots.Add(CFLevelSlot.SkillFeat);
                break;
            case 3:
                slots.Add(CFLevelSlot.SkillIncrease);
                slots.Add(CFLevelSlot.GeneralFeat);
                break;
            case 5:
                slots.Add(CFLevelSlot.AncestryFeat);
                slots.Add(CFLevelSlot.SkillIncrease);
                slots.Add(CFLevelSlot.Boosts);
                break;
        }

        if (build.Class is { } classId && prototypes.TryIndex(classId, out CFClassPrototype? cls))
        {
            if (cls.BonusSkillFeats.Contains(level))
                slots.Add(CFLevelSlot.BonusSkillFeat);
            if (cls.BonusSkillIncreases.Contains(level))
                slots.Add(CFLevelSlot.BonusSkillIncrease);
        }

        return slots;
    }

    /// <summary>The pick in a slot, or null.</summary>
    public static string? Pick(CFCharacterBuildData build, CFLevelSlot slot, int level)
    {
        return slot switch
        {
            CFLevelSlot.ClassFeat => level == 2 ? build.ClassFeatLevel2?.Id : level == 4 ? build.ClassFeatLevel4?.Id : null,
            CFLevelSlot.AncestryFeat => level == 5 ? build.AncestryFeatLevel5?.Id : null,
            CFLevelSlot.SkillFeat => level == 2 ? build.SkillFeatLevel2?.Id : level == 4 ? build.SkillFeatLevel4?.Id : null,
            CFLevelSlot.GeneralFeat => level == 3 ? build.GeneralFeatLevel3?.Id : null,
            CFLevelSlot.BonusSkillFeat => build.BonusSkillFeats.GetValueOrDefault(level),
            CFLevelSlot.SkillIncrease => level == 3 ? build.SkillIncreaseLevel3?.Id : level == 5 ? build.SkillIncreaseLevel5?.Id : null,
            CFLevelSlot.BonusSkillIncrease => build.BonusSkillIncreases.GetValueOrDefault(level),
            _ => null,
        };
    }

    /// <summary>Sets the pick in a slot. Null clears it.</summary>
    public static void SetPick(CFCharacterBuildData build, CFLevelSlot slot, int level, string? id)
    {
        ProtoId<CFFeatPrototype>? feat = id == null ? null : (ProtoId<CFFeatPrototype>?) new ProtoId<CFFeatPrototype>(id);
        ProtoId<CFAbilitySkillPrototype>? skill = id == null ? null : (ProtoId<CFAbilitySkillPrototype>?) new ProtoId<CFAbilitySkillPrototype>(id);
        switch (slot)
        {
            case CFLevelSlot.ClassFeat when level == 2:
                build.ClassFeatLevel2 = feat;
                break;
            case CFLevelSlot.ClassFeat when level == 4:
                build.ClassFeatLevel4 = feat;
                break;
            case CFLevelSlot.AncestryFeat when level == 5:
                build.AncestryFeatLevel5 = feat;
                break;
            case CFLevelSlot.SkillFeat when level == 2:
                build.SkillFeatLevel2 = feat;
                break;
            case CFLevelSlot.SkillFeat when level == 4:
                build.SkillFeatLevel4 = feat;
                break;
            case CFLevelSlot.GeneralFeat when level == 3:
                build.GeneralFeatLevel3 = feat;
                break;
            case CFLevelSlot.SkillIncrease when level == 3:
                build.SkillIncreaseLevel3 = skill;
                break;
            case CFLevelSlot.SkillIncrease when level == 5:
                build.SkillIncreaseLevel5 = skill;
                break;
            case CFLevelSlot.BonusSkillFeat:
                SetOrRemove(build.BonusSkillFeats, level, id);
                break;
            case CFLevelSlot.BonusSkillIncrease:
                SetOrRemove(build.BonusSkillIncreases, level, id);
                break;
        }
    }

    private static void SetOrRemove(Dictionary<int, string> map, int level, string? id)
    {
        if (id == null)
            map.Remove(level);
        else
            map[level] = id;
    }

    /// <summary>The feats a feat slot may take at this level, as the lobby used to offer them.</summary>
    public static List<CFFeatPrototype> FeatOptions(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        CFLevelSlot slot,
        int level)
    {
        var current = Pick(build, slot, level);
        var options = new List<CFFeatPrototype>();
        switch (slot)
        {
            case CFLevelSlot.ClassFeat:
            {
                var taken = new HashSet<string?> { build.ClassFeat?.Id, build.ClassFeatLevel2?.Id, build.ClassFeatLevel4?.Id };
                taken.Remove(current);
                foreach (var feat in prototypes.EnumeratePrototypes<CFFeatPrototype>())
                {
                    if (feat.FitsClass(build.Class?.Id, level)
                        && !taken.Contains(feat.ID)
                        && CFClassChoices.MeetsRequirement(prototypes, build, feat)
                        && CFSkills.MeetsPrerequisites(prototypes, build, ancestryId, feat, level))
                    {
                        options.Add(feat);
                    }
                }

                break;
            }
            case CFLevelSlot.AncestryFeat:
            {
                foreach (var feat in prototypes.EnumeratePrototypes<CFFeatPrototype>())
                {
                    if (ancestryId != null
                        && feat.Fits(ancestryId, level)
                        && feat.ID != build.AncestryFeat?.Id
                        && CFSkills.MeetsPrerequisites(prototypes, build, ancestryId, feat, level))
                    {
                        options.Add(feat);
                    }
                }

                break;
            }
            case CFLevelSlot.SkillFeat:
            case CFLevelSlot.GeneralFeat:
            case CFLevelSlot.BonusSkillFeat:
            {
                var skillSlot = slot != CFLevelSlot.GeneralFeat;
                var taken = new HashSet<string?> { build.SkillFeatLevel2?.Id, build.GeneralFeatLevel3?.Id, build.SkillFeatLevel4?.Id };
                taken.UnionWith(build.BonusSkillFeats.Values);
                taken.UnionWith(CFSkills.BackgroundFeats(prototypes, build).Select(id => id.Id));
                taken.Remove(current);
                foreach (var feat in prototypes.EnumeratePrototypes<CFFeatPrototype>())
                {
                    if ((skillSlot ? feat.FitsSkillSlot(level) : feat.FitsGeneralSlot(level))
                        && !taken.Contains(feat.ID)
                        && CFSkills.Qualifies(prototypes, build, ancestryId, feat, level))
                    {
                        options.Add(feat);
                    }
                }

                break;
            }
        }

        return options
            .OrderBy(feat => feat.Level)
            .ThenBy(feat => feat.ID, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Skills a skill increase may raise: those still below Expert before this level.</summary>
    public static List<CFAbilitySkillPrototype> SkillOptions(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level)
    {
        return prototypes.EnumeratePrototypes<CFAbilitySkillPrototype>()
            .Where(skill => CFSkills.BuildRank(prototypes, build, ancestryId, skill.ID, level - 1) < CFProficiencyRank.Expert)
            .OrderBy(skill => skill.Order)
            .ToList();
    }

    /// <summary>
    /// Whether level 5's boosts teach a language: they raise Intelligence. <paramref name="modifiers"/> are the
    /// character's before those boosts.
    /// </summary>
    public static bool NeedsLanguage(CFCharacterBuildData build, int level, IReadOnlyDictionary<CFAbility, int> modifiers)
    {
        return level == 5 && CFLanguageRules.LevelFiveLanguage(modifiers, build.AbilityBoostsLevel5);
    }

    /// <summary>The spell slots a caster gains at this level, each still to be filled.</summary>
    public static List<CFNewSpellSlot> NewSpellSlots(IPrototypeManager prototypes, CFCharacterBuildData build, int level)
    {
        var result = new List<CFNewSpellSlot>();
        if (build.Class is not { } classId
            || !prototypes.TryIndex(classId, out CFClassPrototype? cls)
            || cls.Spellcasting is not { } casting)
        {
            return result;
        }

        var before = casting.AllSlotsAt(level - 1);
        var after = casting.AllSlotsAt(level);
        for (var rank = 0; rank < after.Count; rank++)
        {
            var had = rank < before.Count ? before[rank] : 0;
            for (var index = had; index < after[rank]; index++)
            {
                result.Add(new CFNewSpellSlot(rank, index));
            }
        }

        return result;
    }

    /// <summary>The option choices (cantrips, skills and the like) that this level's new feats and features ask for.</summary>
    public static List<CFOptionSlot> NewOptionSlots(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level)
    {
        var before = CFOptionChoices.Slots(prototypes, build, ancestryId, level - 1);
        return CFOptionChoices.Slots(prototypes, build, ancestryId, level)
            .Where(slot => !before.Exists(old => old.Source == slot.Source && old.Slot == slot.Slot))
            .ToList();
    }

    /// <summary>
    /// How many picks are still open for this level. A slot with nothing it could take does not count, so a character
    /// is never stuck. A level can be taken with picks still open: they are skipped, and the server writes a report.
    /// </summary>
    /// <param name="knownLanguages">Languages the character already speaks, from any source. Null counts only the build's.</param>
    public static int Unfilled(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level,
        IReadOnlyDictionary<CFAbility, int> modifiers,
        IReadOnlyCollection<ProtoId<CFLanguagePrototype>>? knownLanguages = null)
    {
        return UnfilledSlots(prototypes, build, ancestryId, level, modifiers, knownLanguages).Count;
    }

    /// <summary>
    /// Each pick still open for this level, described for a log: what the slot is and how many options it had. The same
    /// rules as <see cref="Unfilled"/>.
    /// </summary>
    public static List<string> UnfilledSlots(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level,
        IReadOnlyDictionary<CFAbility, int> modifiers,
        IReadOnlyCollection<ProtoId<CFLanguagePrototype>>? knownLanguages = null)
    {
        var missing = new List<string>();
        foreach (var slot in SlotsAt(prototypes, build, level))
        {
            switch (slot)
            {
                case CFLevelSlot.Boosts:
                    var boosts = build.AbilityBoostsLevel5.Distinct().Count();
                    if (boosts < CFAttributeMath.LevelFiveBoostCount)
                        missing.Add($"{slot}: {boosts} of {CFAttributeMath.LevelFiveBoostCount} picked");
                    break;
                case CFLevelSlot.SkillIncrease:
                case CFLevelSlot.BonusSkillIncrease:
                    if (Pick(build, slot, level) == null
                        && SkillOptions(prototypes, build, ancestryId, level).Count is var skills and > 0)
                    {
                        missing.Add($"{slot}: none picked, {skills} options");
                    }
                    break;
                default:
                    if (Pick(build, slot, level) == null
                        && FeatOptions(prototypes, build, ancestryId, slot, level).Count is var feats and > 0)
                    {
                        missing.Add($"{slot}: none picked, {feats} options");
                    }
                    break;
            }
        }

        if (NeedsLanguage(build, level, modifiers) && LevelFiveLanguage(prototypes, build, ancestryId, modifiers) == null)
        {
            var languages = LanguageOptions(prototypes, build, ancestryId, modifiers, knownLanguages).Count;
            if (languages > 0)
                missing.Add($"{CFLevelSlot.Language}: none picked, {languages} options");
        }

        foreach (var spell in NewSpellSlots(prototypes, build, level))
        {
            if (!build.Spells.Exists(pick => pick.Rank == spell.Rank && pick.Index == spell.Index && pick.HasSpell))
                missing.Add($"Spell: rank {spell.Rank}, slot {spell.Index}, none picked");
        }

        foreach (var option in NewOptionSlots(prototypes, build, ancestryId, level))
        {
            if (build.OptionPicks.Exists(pick => pick.Source == option.Source && pick.Slot == option.Slot))
                continue;

            var allowed = CFOptionChoices.Allowed(prototypes, option.Choice, build).Count;
            if (allowed > 0)
                missing.Add($"Option: {option.Choice.Kind} for {option.Source}, slot {option.Slot}, none picked, {allowed} options");
        }

        return missing;
    }

    /// <summary>
    /// The languages level 5's Intelligence boost could teach: common ones the ancestry does not give and the character
    /// does not already speak. The pick itself stays an option, so a list can show it as chosen.
    /// </summary>
    /// <param name="knownLanguages">Languages the character already speaks, from any source, before this level.</param>
    public static List<CFLanguagePrototype> LanguageOptions(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        IReadOnlyDictionary<CFAbility, int> modifiers,
        IReadOnlyCollection<ProtoId<CFLanguagePrototype>>? knownLanguages = null)
    {
        CFAncestryPrototype? ancestry = null;
        if (ancestryId != null)
            prototypes.TryIndex(ancestryId, out ancestry);

        var known = CFLanguageRules.Known(prototypes, ancestry, build.Languages, modifiers);
        return CFLanguageRules.Pickable(prototypes, ancestry)
            .Where(language => !known.Contains(language.ID)
                               && knownLanguages?.Contains(language.ID) != true)
            .ToList();
    }

    /// <summary>
    /// Spells the build gets by <paramref name="level"/> from its ancestry, heritage, background, class, subclass, feats
    /// and their choices, rather than from spell slots. The spell tabs list them so a player does not pick one twice.
    /// </summary>
    public static List<CFGrantedSpell> GrantedSpells(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level)
    {
        var result = new List<CFGrantedSpell>();

        void FromGrants(IEnumerable<Grants.CFGrant> grants, LocId source)
        {
            foreach (var grant in grants)
            {
                var spell = grant switch
                {
                    Grants.CFGrantSpell plain => plain.Spell.Id,
                    Grants.CFGrantFocusSpell focus => focus.Spell.Id,
                    _ => null,
                };

                if (spell != null && !result.Exists(granted => granted.Spell == spell))
                    result.Add(new CFGrantedSpell(spell, source));
            }
        }

        if (ancestryId != null && prototypes.TryIndex(ancestryId, out CFAncestryPrototype? ancestry))
            FromGrants(ancestry.Grants, ancestry.Name);

        if (build.Heritage is { } heritageId
            && prototypes.TryIndex(heritageId, out CFHeritagePrototype? heritage)
            && heritage.Fits(ancestryId))
        {
            FromGrants(heritage.Grants, heritage.Name);
        }

        if (build.Background is { } backgroundId && prototypes.TryIndex(backgroundId, out CFBackgroundPrototype? background))
            FromGrants(background.Grants, background.Name);

        if (build.Class is { } classId && prototypes.TryIndex(classId, out CFClassPrototype? cls))
        {
            foreach (var feature in cls.Features)
            {
                if (feature.Level <= level)
                    FromGrants(feature.Grants, cls.Name);
            }
        }

        foreach (var choice in CFClassChoices.Selected(prototypes, build))
        {
            FromGrants(choice.Grants, choice.Name);
            foreach (var feature in choice.Features)
            {
                if (feature.Level <= level)
                    FromGrants(feature.Grants, choice.Name);
            }
        }

        foreach (var feat in CFSkills.SelectedFeats(prototypes, build, ancestryId, build.Class?.Id, level))
        {
            FromGrants(feat.Grants, feat.Name);
        }

        // A cantrip picked for an option, such as Adapted Cantrip.
        foreach (var slot in CFOptionChoices.Slots(prototypes, build, ancestryId, level))
        {
            if (slot.Choice.Kind != CFOptionChoiceKind.Cantrip)
                continue;

            var pick = build.OptionPicks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot);
            if (pick != null && prototypes.HasIndex<CFSpellPrototype>(pick.Value) && !result.Exists(granted => granted.Spell == pick.Value))
                result.Add(new CFGrantedSpell(pick.Value, slot.SourceName));
        }

        return result;
    }

    /// <summary>The language level 5's Intelligence boost teaches, if the build has picked one.</summary>
    public static string? LevelFiveLanguage(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        IReadOnlyDictionary<CFAbility, int> modifiers)
    {
        CFAncestryPrototype? ancestry = null;
        if (ancestryId != null)
            prototypes.TryIndex(ancestryId, out ancestry);

        return CFLanguageRules.LevelFivePick(prototypes, ancestry, build.Languages, modifiers)?.Id;
    }

    /// <summary>
    /// Copies the picks for <paramref name="level"/> from <paramref name="sent"/>, which came from a client, onto
    /// <paramref name="build"/>, the character's own. Nothing for any other level is taken. The server still checks every
    /// pick, as it does at spawn.
    /// </summary>
    public static void TakeLevel(IPrototypeManager prototypes, CFCharacterBuildData build, CFCharacterBuildData sent, int level)
    {
        foreach (var slot in SlotsAt(prototypes, build, level))
        {
            if (slot == CFLevelSlot.Boosts)
            {
                build.AbilityBoostsLevel5 = sent.AbilityBoostsLevel5.Distinct().Take(CFAttributeMath.LevelFiveBoostCount).ToList();
                continue;
            }

            SetPick(build, slot, level, Pick(sent, slot, level));
        }

        // Level 5 may teach one language. Only new ones are taken, after those the character already has.
        if (level == 5)
        {
            foreach (var language in sent.Languages)
            {
                if (!build.Languages.Contains(language))
                    build.Languages.Add(language);
            }
        }

        foreach (var slot in NewSpellSlots(prototypes, build, level))
        {
            build.Spells.RemoveAll(pick => pick.Rank == slot.Rank && pick.Index == slot.Index);
            var chosen = sent.Spells.FirstOrDefault(pick => pick.Rank == slot.Rank && pick.Index == slot.Index);
            if (chosen != null && chosen.HasSpell)
                build.Spells.Add(chosen.Copy());
        }

        foreach (var pick in sent.OptionPicks)
        {
            if (!build.OptionPicks.Exists(old => old.Source == pick.Source && old.Slot == pick.Slot))
                build.OptionPicks.Add(pick.Copy());
        }

        build.Level = level;
    }
}

/// <summary>A spell the character gets from something other than its spell slots, and what gives it.</summary>
public readonly record struct CFGrantedSpell(string Spell, LocId Source);

/// <summary>
/// A player takes their next level, with the picks they made on the character sheet. Only that level's picks are read.
/// </summary>
[Serializable, NetSerializable]
public sealed class CFLevelUpRequestEvent(CFCharacterBuildData build) : EntityEventArgs
{
    public readonly CFCharacterBuildData Build = build;
}
