using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.NameIdentifier;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server.CF.Companions;

/// <summary>
/// Manifests a Summoner's eidolon, works out its statistics, keeps the two bodies on one pool of Hit Points, and holds it to the 100-foot tether.
/// Once manifested it is an ordinary companion, ordered by pointing, and a ghost can take it over.
/// </summary>
public sealed partial class CFEidolonServerSystem : EntitySystem
{
    private const string Prototype = "CFEidolon";
    private const float TetherTiles = 20f;
    private const int Trained = 2;
    private static readonly TimeSpan CheckTether = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFCompanionSystem _companions = default!;
    [Dependency] private CFDyingSystem _dying = default!;

    [SubscribeLocalEvent]
    private void OnManifest(Entity<CFManifestEidolonActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled || !TryComp(args.Performer, out CFEidolonLinkComponent? link))
            return;

        args.Handled = true;
        Toggle(args.Performer, link);
    }

    /// <summary>Manifests the eidolon, or unmanifests it if it is already here. False if the Summoner is down.</summary>
    public bool Toggle(EntityUid summoner, CFEidolonLinkComponent link)
    {
        if (link.Eidolon.IsValid() && Exists(link.Eidolon))
        {
            Unmanifest(summoner, link);
            return true;
        }

        if (!TryComp(summoner, out CFCombatantComponent? combat) || combat.CurrentHp <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-eidolon-cannot"), summoner, summoner);
            return false;
        }

        var eidolon = Spawn(Prototype, Transform(summoner).Coordinates.Offset(new Vector2(1f, 0f)));
        _companions.Bind(summoner, eidolon);
        link.Eidolon = eidolon;
        link.SharedHp = combat.CurrentHp;

        // The stock mob base numbers its name, as if this were one of a crowd.
        RemComp<NameIdentifierComponent>(eidolon);
        _metaData.SetEntityName(eidolon, link.Name);
        ApplyStats(eidolon, link, combat);

        if (link.Resistance is { } resistance)
            _bonuses.AddPermanentResistance(eidolon, resistance, 0, halfLevel: true);

        if (link.VoidHealing)
            EnsureComp<CFVoidHealingComponent>(eidolon);

        _companions.ComeAlong(eidolon);
        return true;
    }

    [Dependency] private MetaDataSystem _metaData = default!;

    public void Unmanifest(EntityUid summoner, CFEidolonLinkComponent link)
    {
        if (Exists(link.Eidolon))
            QueueDel(link.Eidolon);

        link.Eidolon = EntityUid.Invalid;
    }

    /// <summary>What the eidolon is at this level. It uses the Summoner's level and Hit Points, and its own attributes.</summary>
    public void ApplyStats(EntityUid eidolon, CFEidolonLinkComponent link, CFCombatantComponent summoner)
    {
        var key = 4;
        var other = 2;
        var dexterity = link.DexterityKey ? key : other;
        var strength = link.DexterityKey ? other : key;
        var dexCap = link.DexterityKey ? 4 : 3;
        var item = link.DexterityKey ? 1 : 2;

        var abilities = EnsureComp<CFAbilityScoreComponent>(eidolon);
        abilities.AbilityModifiers[CFAbility.Strength] = strength;
        abilities.AbilityModifiers[CFAbility.Dexterity] = dexterity;
        abilities.AbilityModifiers[CFAbility.Constitution] = link.Constitution;
        abilities.AbilityModifiers[CFAbility.Intelligence] = link.Intelligence;
        abilities.AbilityModifiers[CFAbility.Wisdom] = link.Wisdom;
        abilities.AbilityModifiers[CFAbility.Charisma] = link.Charisma;
        Dirty(eidolon, abilities);

        var combat = EnsureComp<CFCombatantComponent>(eidolon);
        combat.Level = summoner.Level;
        combat.ArmorClass = 10 + summoner.Level + Trained + Math.Min(dexterity, dexCap) + item;
        combat.MaxHp = summoner.MaxHp;
        combat.CurrentHp = link.SharedHp;
        combat.SpeedFeet = link.SpeedFeet + link.ExtraSpeed;
        combat.ProficiencyRanks["Unarmed"] = summoner.Level >= 5 ? CFProficiencyRank.Expert : CFProficiencyRank.Trained;
        combat.ProficiencyRanks["Perception"] = summoner.Level >= 3 ? CFProficiencyRank.Expert : CFProficiencyRank.Trained;
        combat.ProficiencyRanks["Fortitude"] = CFProficiencyRank.Trained;
        combat.ProficiencyRanks["Reflex"] = CFProficiencyRank.Trained;
        combat.ProficiencyRanks["Will"] = CFProficiencyRank.Trained;
        combat.ProficiencyRanks["Unarmored"] = CFProficiencyRank.Trained;
        Dirty(eidolon, combat);

        var options = EnsureComp<CFCombatOptionsComponent>(eidolon);
        options.NaturalDice = 1;
        options.NaturalDie = 8;
        options.NaturalType = link.AttackType;
        options.NaturalFinesse = link.DexterityKey;
        options.NaturalAgile = false;
        Dirty(eidolon, options);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFEidolonLinkComponent, CFCombatantComponent>();
        while (query.MoveNext(out var uid, out var link, out var summoner))
        {
            if (!link.Eidolon.IsValid())
                continue;

            if (TerminatingOrDeleted(link.Eidolon) || !TryComp(link.Eidolon, out CFCombatantComponent? eidolon))
            {
                link.Eidolon = EntityUid.Invalid;
                continue;
            }

            Share(uid, link, summoner, eidolon);
            if (!link.Eidolon.IsValid() || now < link.NextTether)
                continue;

            link.NextTether = now + CheckTether;
            if (eidolon.Level != summoner.Level || eidolon.MaxHp != summoner.MaxHp)
                ApplyStats(link.Eidolon, link, summoner);

            var apart = _transform.GetMapCoordinates(uid).MapId != _transform.GetMapCoordinates(link.Eidolon).MapId
                        || (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(link.Eidolon)).Length() > TetherTiles;
            if (apart)
            {
                Unmanifest(uid, link);
                _popup.PopupEntity(Loc.GetString("cf-eidolon-tether"), uid, uid);
            }
        }
    }

    /// <summary>
    /// One pool of Hit Points: whatever happened to either body since last time happens to both. It is done by comparing, so that nothing that deals damage or heals has to know about eidolons.
    /// </summary>
    private void Share(EntityUid uid, CFEidolonLinkComponent link, CFCombatantComponent summoner, CFCombatantComponent eidolon)
    {
        var total = link.SharedHp + (summoner.CurrentHp - link.SharedHp) + (eidolon.CurrentHp - link.SharedHp);
        if (total == link.SharedHp && summoner.CurrentHp == eidolon.CurrentHp)
            return;

        total = Math.Clamp(total, 0, summoner.MaxHp);
        var summonerWasUp = summoner.CurrentHp > 0;
        link.SharedHp = total;
        summoner.CurrentHp = total;
        eidolon.CurrentHp = total;
        Dirty(link.Eidolon, eidolon);
        Dirty(uid, summoner);
        _dying.SyncHealthBar(uid, summoner);
        _dying.SyncHealthBar(link.Eidolon, eidolon);

        if (total > 0)
            return;

        // Falling to 0 ends the manifestation. A blow to the eidolon has to knock the Summoner out as well, since nothing else will.
        if (summonerWasUp)
            _dying.KnockOut(uid, summoner, fromCriticalHit: false);

        Unmanifest(uid, link);
    }
}
