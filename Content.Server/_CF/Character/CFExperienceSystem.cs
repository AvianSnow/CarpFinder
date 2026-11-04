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
        var factionId = CompOrNull<CFFactionComponent>(user)?.Faction.Id;
        var partyLevel = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
        if (factionId != null)
        {
            var query = EntityQueryEnumerator<CFExperienceComponent, CFFactionComponent, CFCombatantComponent>();
            while (query.MoveNext(out _, out _, out var faction, out var combat))
            {
                if (faction.Faction.Id == factionId)
                    partyLevel = Math.Max(partyLevel, combat.Level);
            }
        }

        if (!CFExperienceAwards.TryHazard(complex, hazardLevel - partyLevel, out var xp) || xp <= 0)
            return 0;

        if (factionId != null && AwardFaction(factionId, xp).Count > 0)
            return xp;

        AddXp(user, xp);
        return xp;
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
        var gain = 1;
        if (combat.ClassId != null
            && _prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls))
        {
            var con = TryComp(mob, out CFAbilityScoreComponent? scores)
                ? scores.GetModifier(CFAbility.Constitution)
                : 0;
            gain = Math.Max(1, cls.HitPoints + con);
        }

        combat.MaxHp += gain;
        combat.CurrentHp += gain;
        Dirty(mob, combat);

        foreach (var featId in FeatsAt(xp, combat.Level))
        {
            if (featId != null && _prototypes.TryIndex(featId, out CFFeatPrototype? feat))
                _spawn.ApplyFeat(mob, feat);
        }

        _spawn.RefreshArmorClass(mob);
        _popup.PopupEntity(Loc.GetString("cf-xp-level", ("level", combat.Level)), mob, mob);
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
        return feats;
    }
}
