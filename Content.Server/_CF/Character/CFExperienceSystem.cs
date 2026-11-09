using Content.Server.CF.Companions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Preferences;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Character;

/// <summary>
/// Gives a company's characters the same XP. Anyone below that company's highest level gains double until they catch up.
/// </summary>
public sealed partial class CFExperienceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFCharacterSpawnSystem _spawn = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;
    [Dependency] private CFCompanionSystem _companions = default!;

    public IEnumerable<string> Factions()
    {
        foreach (var faction in _prototypes.EnumeratePrototypes<CFFactionPrototype>())
            yield return faction.ID;
    }

    public bool TryFindFaction(string name, out CFFactionPrototype? faction)
    {
        foreach (var candidate in _prototypes.EnumeratePrototypes<CFFactionPrototype>())
        {
            if (!string.Equals(candidate.ID, name, StringComparison.OrdinalIgnoreCase))
                continue;

            faction = candidate;
            return true;
        }

        faction = null;
        return false;
    }

    /// <summary>Awards every character in the company. Returns how many gained XP, and how many of those were behind.</summary>
    public (int Count, int Behind) AwardFaction(string factionId, int amount)
    {
        if (amount <= 0 || !TryFindFaction(factionId, out var faction) || faction == null)
            return (0, 0);

        var members = new List<EntityUid>();
        var query = EntityQueryEnumerator<CFExperienceComponent, CFFactionComponent, CFCombatantComponent>();
        while (query.MoveNext(out var uid, out _, out var memberFaction, out _))
        {
            if (memberFaction.Faction.Id == faction.ID)
                members.Add(uid);
        }

        var partyLevel = 1;
        foreach (var uid in members)
            partyLevel = Math.Max(partyLevel, Comp<CFCombatantComponent>(uid).Level);

        var behind = 0;
        foreach (var uid in members)
        {
            var level = Comp<CFCombatantComponent>(uid).Level;
            var grant = amount;
            if (level < partyLevel)
            {
                grant = amount * 2;
                behind++;
            }

            AddXp(uid, grant);
        }

        return (members.Count, behind);
    }

    /// <summary>
    /// XP for overcoming a hazard, by its level against the party level (the highest level in the company).
    /// The whole company gets it. A creature with no company gets it alone. Hazards more than 4 levels
    /// below the party are worth nothing.
    /// </summary>
    public int AwardHazard(EntityUid user, int hazardLevel, bool complex)
    {
        CFExperienceAwards.TryHazard(complex, hazardLevel - PartyLevel(user), out var xp);
        return Award(user, xp);
    }

    /// <summary>XP for defeating a creature, by its level against the party level. Shared like hazard XP.</summary>
    public int AwardCreature(EntityUid user, int creatureLevel)
    {
        CFExperienceAwards.TryAdversary(creatureLevel - PartyLevel(user), out var xp);
        return Award(user, xp);
    }

    private int PartyLevel(EntityUid user)
    {
        var factionId = CompOrNull<CFFactionComponent>(user)?.Faction.Id;
        var partyLevel = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
        if (factionId == null)
            return partyLevel;

        var query = EntityQueryEnumerator<CFExperienceComponent, CFFactionComponent, CFCombatantComponent>();
        while (query.MoveNext(out _, out _, out var faction, out var combat))
        {
            if (faction.Faction.Id == factionId)
                partyLevel = Math.Max(partyLevel, combat.Level);
        }

        return partyLevel;
    }

    /// <summary>The user's company: its highest level, and how many characters are in it.</summary>
    public (int Level, int Size) Party(EntityUid user)
    {
        var size = 0;
        var factionId = CompOrNull<CFFactionComponent>(user)?.Faction.Id;
        if (factionId != null)
        {
            var query = EntityQueryEnumerator<CFExperienceComponent, CFFactionComponent>();
            while (query.MoveNext(out _, out var faction))
            {
                if (faction.Faction.Id == factionId)
                    size++;
            }
        }

        return (PartyLevel(user), Math.Max(1, size));
    }

    private int Award(EntityUid user, int xp)
    {
        if (xp <= 0)
            return 0;

        var factionId = CompOrNull<CFFactionComponent>(user)?.Faction.Id;
        if (factionId != null && AwardFaction(factionId, xp).Count > 0)
            return xp;

        AddXp(user, xp);
        return xp;
    }

    [SubscribeLocalEvent]
    private void OnCreatureDefeated(Entity<CFCombatantComponent> ent, ref CFCreatureDefeatedEvent args)
    {
        AwardCreature(args.Killer, args.Level);
    }

    [SubscribeLocalEvent]
    private void OnHazardDisarmed(Entity<CFHazardComponent> ent, ref CFHazardDisarmedEvent args)
    {
        AwardHazard(args.User, args.Level, args.Complex);
    }

    public void AddXp(EntityUid mob, int amount)
    {
        if (amount <= 0 || !TryComp(mob, out CFExperienceComponent? xp) || !TryComp(mob, out CFCombatantComponent? combat))
            return;

        xp.Xp += amount;
        _popup.PopupEntity(Loc.GetString("cf-xp-gained", ("xp", amount)), mob, mob);
        while (combat.Level < CFExperienceComponent.MaxLevel && xp.Xp >= CFExperienceComponent.XpPerLevel)
        {
            xp.Xp -= CFExperienceComponent.XpPerLevel;
            LevelUp(mob, combat, xp);
        }

        Dirty(mob, xp);
    }

    private void LevelUp(EntityUid mob, CFCombatantComponent combat, CFExperienceComponent xp)
    {
        combat.Level++;
        var constitutionRaised = ApplyLevelChoices(mob, combat, xp);
        var gain = 1;
        CFClassPrototype? cls = null;
        if (combat.ClassId != null && _prototypes.TryIndex(combat.ClassId, out cls))
        {
            var con = TryComp(mob, out CFAbilityScoreComponent? scores)
                ? scores.GetModifier(CFAbility.Constitution)
                : 0;
            gain = Math.Max(1, cls.HitPoints + con);
        }

        // Constitution counts for every level, so a rise pays back the earlier ones too.
        if (constitutionRaised)
            gain += combat.Level - 1;

        combat.MaxHp += gain;
        combat.CurrentHp += gain;
        Dirty(mob, combat);

        foreach (var featId in FeatsAt(xp, combat.Level))
        {
            if (featId != null && _prototypes.TryIndex(featId, out CFFeatPrototype? feat))
                _spawn.ApplyFeat(mob, feat);
        }

        if (cls != null)
        {
            _spawn.GrowSpellbook(mob, cls, combat.Level, xp);
            _features.ApplyClassFeatures(mob, cls, combat.Level);
        }

        foreach (var choiceId in xp.ClassChoices)
        {
            if (_prototypes.TryIndex(choiceId, out CFClassChoicePrototype? choice))
                _features.ApplyClassChoice(mob, choice, combat.Level);
        }

        _spawn.RefreshArmorClass(mob);
        _companions.MatchOwnerLevel(mob, combat);
        _popup.PopupEntity(Loc.GetString("cf-xp-level", ("level", combat.Level)), mob, mob);
    }

    /// <summary>The skill increase and boosts picked in the lobby for this level. True if Constitution went up.</summary>
    private bool ApplyLevelChoices(EntityUid mob, CFCombatantComponent combat, CFExperienceComponent xp)
    {
        var increase = combat.Level switch
        {
            3 => xp.SkillIncreaseLevel3,
            5 => xp.SkillIncreaseLevel5,
            _ => null,
        };

        if (!string.IsNullOrEmpty(increase))
            combat.SkillIncreases.Add(increase);

        if (xp.BonusSkillIncreases.TryGetValue(combat.Level, out var bonusIncrease))
            combat.SkillIncreases.Add(bonusIncrease);

        if (combat.Level != 5 || !TryComp(mob, out CFAbilityScoreComponent? abilities))
            return false;

        var constitutionRaised = false;
        foreach (var ability in xp.AbilityBoostsLevel5)
        {
            if (CFAttributeMath.TryLevelBoost(abilities.AbilityModifiers, ability) && ability == CFAbility.Constitution)
                constitutionRaised = true;
        }

        Dirty(mob, abilities);
        return constitutionRaised;
    }

    private static List<string> FeatsAt(CFExperienceComponent xp, int level)
    {
        var feats = new List<string>();
        string? id = level switch
        {
            1 => null,
            2 => xp.ClassFeatLevel2,
            4 => xp.ClassFeatLevel4,
            5 => xp.AncestryFeatLevel5,
            _ => null,
        };

        if (level == 1)
        {
            if (!string.IsNullOrEmpty(xp.AncestryFeat))
                feats.Add(xp.AncestryFeat);
            if (!string.IsNullOrEmpty(xp.ClassFeat))
                feats.Add(xp.ClassFeat);
            return feats;
        }

        if (!string.IsNullOrEmpty(id))
            feats.Add(id);

        var general = level switch
        {
            2 => xp.SkillFeatLevel2,
            3 => xp.GeneralFeatLevel3,
            4 => xp.SkillFeatLevel4,
            _ => null,
        };

        if (!string.IsNullOrEmpty(general))
            feats.Add(general);

        if (xp.BonusSkillFeats.TryGetValue(level, out var bonus))
            feats.Add(bonus);

        return feats;
    }
}
