using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>A bonus an ikon gives while it holds the divine spark.</summary>
[DataDefinition]
public sealed partial class CFIkonBonus
{
    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField]
    public CFBonusType Type = CFBonusType.Status;

    [DataField(required: true)]
    public int Value;

    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? Skill;
}

/// <summary>An Exemplar's ikon: what it has to be, what it does while it holds the spark, and its transcendence.</summary>
[Prototype("cfIkon")]
public sealed partial class CFIkonPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>The action that moves the spark here.</summary>
    [DataField(required: true)]
    public EntProtoId Shift;

    /// <summary>Null when it is not built yet.</summary>
    [DataField]
    public EntProtoId? Transcendence;

    /// <summary>A melee weapon can be this ikon. No usage at all means a body or worn ikon, which is always ready.</summary>
    [DataField]
    public bool Melee;

    [DataField]
    public bool Ranged;

    [DataField]
    public bool Thrown;

    [DataField]
    public bool Unarmed;

    /// <summary>It only works with a shield in hand.</summary>
    [DataField]
    public bool Shield;

    /// <summary>A weapon must be in one of these groups. Empty means any.</summary>
    [DataField]
    public List<CFWeaponGroup> Groups = new();

    /// <summary>A weapon or unarmed attack must deal one of these. Empty means any.</summary>
    [DataField]
    public List<string> DamageTypes = new();

    [DataField]
    public int SpiritPerDie;

    /// <summary>Replaces <see cref="SpiritPerDie"/> against a creature below half its Hit Points. 0 means no change.</summary>
    [DataField]
    public int BelowHalfPerDie;

    /// <summary>Replaces <see cref="SpiritPerDie"/> against an off-guard creature. 0 means no change.</summary>
    [DataField]
    public int OffGuardPerDie;

    [DataField]
    public int SplashPerDie;

    [DataField]
    public int PersistentPerDie;

    /// <summary>On a critical hit, this die per weapon die replaces the usual doubled damage. 0 means no change.</summary>
    [DataField]
    public int CriticalDie;

    [DataField]
    public List<CFIkonBonus> Bonuses = new();

    /// <summary>Resistance equal to half the Exemplar's level.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? Resistance;

    /// <summary>Give each of these <c>needs: Immanence</c> and this ikon's id.</summary>
    [DataField]
    public List<CFAuraEffect> Aura = new();

    [DataField]
    public int AuraFeet;

    public bool IsWeapon => Melee || Ranged || Thrown || Unarmed;
}

public enum CFEpithet : byte
{
    None,
    Brave,
    Radiant,
}

/// <summary>An Exemplar: three ikons, and which one holds the spark.</summary>
[RegisterComponent]
public sealed partial class CFExemplarComponent : Component
{
    [DataField]
    public List<ProtoId<CFIkonPrototype>> Ikons = new();

    /// <summary>An index of <see cref="Ikons"/>, or -1 when the spark is in the soul.</summary>
    [DataField]
    public int Spark = -1;

    [DataField]
    public string SparkType = "Spirit";

    [DataField]
    public CFEpithet Epithet;

    [DataField]
    public EntityUid LastHit;

    [DataField]
    public int LastDamage;

    [DataField]
    public TimeSpan LastHitAt;

    [DataField]
    public TimeSpan LastTranscendence;

    /// <summary>Extra d6 on the next Strike that hits (A Moment Unending).</summary>
    [DataField]
    public int NextStrikeDice;

    [DataField]
    public TimeSpan NextStrikeUntil;

    [DataField]
    public TimeSpan NextCheck;
}

/// <summary>Someone a Radiant Exemplar has healed, who cannot be healed that way again for 10 minutes.</summary>
[RegisterComponent]
public sealed partial class CFRadiantHealedComponent : Component
{
    [DataField]
    public TimeSpan Until;
}

/// <summary>An action that moves the spark into an ikon.</summary>
[RegisterComponent]
public sealed partial class CFShiftImmanenceActionComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CFIkonPrototype> Ikon;
}

/// <summary>Marks a Spark Transcendence action. It is only enabled while it can be used, and throws the spark out when it has been.</summary>
[RegisterComponent]
public sealed partial class CFIkonTranscendenceComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CFIkonPrototype> Ikon;

    /// <summary>The Exemplar must have just hit something with a Strike.</summary>
    [DataField]
    public bool NeedsLastHit;

    /// <summary>Resolves the action's <c>CFAreaEffect</c>, at the target or around the Exemplar.</summary>
    [DataField]
    public bool Area;
}

public enum CFIkonDeed : byte
{
    DrinkOfMyFoes,
    StrikeBreatheRend,
    MomentUnending,
    NoScarButThis,
    CrashAgainstMe,
}

/// <summary>A transcendence that needs what the Exemplar has just done.</summary>
public sealed partial class CFIkonEffect : CFEffect
{
    [DataField(required: true)]
    public CFIkonDeed Deed;

    /// <summary>Crash against Me: the ikon whose damage type is resisted.</summary>
    [DataField]
    public ProtoId<CFIkonPrototype>? Ikon;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFIkonSystem>().Perform(Deed, args.User, Ikon);
    }
}

/// <summary>Spirit damage equal to the Exemplar's normal Strike damage, scaled by the target's save.</summary>
public sealed partial class CFIkonStrikeDamageEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFIkonSystem>().DealStrikeDamage(args, target);
    }
}

/// <summary>Divine sparks, ikons and transcendence.</summary>
public sealed partial class CFIkonSystem : EntitySystem
{
    private const string Source = "ikon";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan HitStaysRecent = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Round = TimeSpan.FromSeconds(6);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFAuraSystem _auras = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFCharacterFeatureSystem _features = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;

    public void Add(EntityUid mob, ProtoId<CFIkonPrototype> id)
    {
        if (!_prototypes.TryIndex(id, out var ikon))
            return;

        var comp = EnsureComp<CFExemplarComponent>(mob);
        if (comp.Ikons.Contains(id))
            return;

        comp.Ikons.Add(id);
        _features.GrantAction(mob, ikon.Shift);
        if (ikon.Transcendence is { } transcendence)
            _features.GrantAction(mob, transcendence);

        if (ikon.Aura.Count > 0)
            _auras.Add(mob, ikon.Aura, ikon.AuraFeet);

        // Without initiative to Shift Immanence on, the spark starts in the first ikon.
        if (comp.Spark < 0)
            SetSpark(mob, comp, 0);

        Refresh(mob, comp);
    }

    public CFIkonPrototype? Empowered(EntityUid mob, CFExemplarComponent? comp = null)
    {
        if (!Resolve(mob, ref comp, false) || comp.Spark < 0 || comp.Spark >= comp.Ikons.Count)
            return null;

        return _prototypes.TryIndex(comp.Ikons[comp.Spark], out var ikon) ? ikon : null;
    }

    /// <summary>True when the spark is in this ikon and the ikon is ready to use.</summary>
    public bool IsEmpowered(EntityUid mob, string ikon)
    {
        return Empowered(mob) is { } current && current.ID == ikon && IsReady(mob, current);
    }

    public void SetSpark(EntityUid mob, int index)
    {
        if (TryComp(mob, out CFExemplarComponent? comp))
            SetSpark(mob, comp, index);
    }

    private void SetSpark(EntityUid mob, CFExemplarComponent comp, int index)
    {
        _bonuses.RemoveBonusesFrom(mob, Source);
        comp.Spark = index;
        if (Empowered(mob, comp) is not { } ikon)
            return;

        foreach (var bonus in ikon.Bonuses)
        {
            _bonuses.AddPermanentBonus(mob, bonus.Stat, bonus.Type, bonus.Value, bonus.Trait, bonus.Skill, Source);
        }
    }

    /// <summary>Whether the ikon can be used right now. A weapon ikon has to be the weapon in hand, or the fists.</summary>
    private bool IsReady(EntityUid mob, CFIkonPrototype ikon)
    {
        if (ikon.Shield)
            return _shields.TryGetUsableShield(mob, out _);

        if (!ikon.IsWeapon)
            return true;

        var item = HeldStats(mob, out var stats);
        return Fits(ikon, stats, item);
    }

    /// <summary>The Strike the Exemplar would make. Returns the weapon, or the Exemplar when striking unarmed.</summary>
    private EntityUid HeldStats(EntityUid mob, out CFStrikeWeaponStats stats)
    {
        if (_hands.TryGetActiveItem(mob, out var held) && _weapons.TryStats(held.Value, out stats))
            return held.Value;

        stats = _strikes.UnarmedStats(mob);
        return mob;
    }

    private bool Fits(CFIkonPrototype ikon, CFStrikeWeaponStats stats, EntityUid weapon)
    {
        if (ikon.DamageTypes.Count > 0 && !ikon.DamageTypes.Exists(type => type.Equals(stats.DamageType, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (stats.Traits.Unarmed)
            return ikon.Unarmed;

        if (ikon.Groups.Count > 0 && (!TryComp(weapon, out CFWeaponComponent? held) || !ikon.Groups.Contains(held.Group)))
            return false;

        if (ikon.Thrown && stats.ThrownStrike)
            return true;

        return stats.Ranged ? ikon.Ranged : ikon.Melee;
    }

    /// <summary>What an empowered weapon ikon adds to a Strike that hit. <paramref name="weapon"/> is the Strike's own weapon.</summary>
    public void NoteHit(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, EntityUid weaponUid, bool critical, int damage)
    {
        if (!_net.IsServer || !TryComp(user, out CFExemplarComponent? comp))
            return;

        comp.LastHit = target;
        comp.LastDamage = damage;
        comp.LastHitAt = _timing.CurTime;

        if (TerminatingOrDeleted(target) || CompOrNull<CFCombatantComponent>(target) is not { CurrentHp: > 0 } defender)
            return;

        if (comp.NextStrikeDice > 0)
        {
            if (_timing.CurTime < comp.NextStrikeUntil)
                _effects.DealDamage(user, target, Roll(comp.NextStrikeDice, 6) * (critical ? 2 : 1), weapon.DamageType, false);

            comp.NextStrikeDice = 0;
        }

        if (Empowered(user, comp) is not { } ikon || !ikon.IsWeapon || !Fits(ikon, weapon, weaponUid))
            return;

        var dice = weapon.DamageDice;
        var perDie = ikon.SpiritPerDie;
        if (ikon.BelowHalfPerDie > 0 && defender.CurrentHp * 2 < defender.MaxHp)
            perDie = ikon.BelowHalfPerDie;

        if (ikon.OffGuardPerDie > 0 && _strikes.IsOffGuardTo(target, user))
            perDie = ikon.OffGuardPerDie;

        var amount = perDie * dice * (critical ? 2 : 1);
        if (critical && ikon.CriticalDie > 0)
            amount = Roll(dice, ikon.CriticalDie);

        if (amount > 0)
            _effects.DealDamage(user, target, amount, comp.SparkType, false);

        if (ikon.PersistentPerDie > 0)
            _bonuses.AddPersistent(target, comp.SparkType, 0, 6, ikon.PersistentPerDie * dice, user);

        if (ikon.SplashPerDie > 0)
            Splash(user, target, comp, ikon.SplashPerDie * dice);
    }

    private void Splash(EntityUid user, EntityUid target, CFExemplarComponent comp, int amount)
    {
        var center = _transform.GetMapCoordinates(target);
        var found = _effects.CollectArea(user, CFAreaShape.Burst, 5, center, center, atPoint: true, includeCenter: true, selfAllowed: false, CFTargetFilter.Enemies);
        foreach (var creature in found)
        {
            _effects.DealDamage(user, creature, amount, comp.SparkType, false);
        }
    }

    private int Roll(int dice, int die)
    {
        var total = 0;
        for (var i = 0; i < dice; i++)
        {
            total += _random.Next(1, die + 1);
        }

        return total;
    }

    public void Perform(CFIkonDeed deed, EntityUid user, ProtoId<CFIkonPrototype>? ikonId)
    {
        if (!_net.IsServer || !TryComp(user, out CFExemplarComponent? comp))
            return;

        var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
        switch (deed)
        {
            case CFIkonDeed.DrinkOfMyFoes:
                _effects.Heal(user, user, comp.LastDamage / 2);
                break;
            case CFIkonDeed.StrikeBreatheRend:
                HeldStats(user, out var stats);
                _effects.DealDamage(user, comp.LastHit, Roll(stats.DamageDice, stats.DamageDie), comp.SparkType, false);
                break;
            case CFIkonDeed.MomentUnending:
                comp.NextStrikeDice = 1;
                comp.NextStrikeUntil = _timing.CurTime + Round * 2;
                break;
            case CFIkonDeed.NoScarButThis:
                _effects.Heal(user, user, Roll(1 + (level - 1) / 2, 8));
                break;
            case CFIkonDeed.CrashAgainstMe:
                if (ikonId is { } id && _prototypes.TryIndex(id, out var ikon) && ikon.Resistance is { } type)
                    _bonuses.AddResistance(user, type, level, weakness: false, (float) Round.TotalSeconds);
                break;
        }
    }

    public void DealStrikeDamage(CFEffectArgs args, EntityUid target)
    {
        if (!_net.IsServer || !TryComp(args.User, out CFAbilityScoreComponent? abilities) || !TryComp(args.User, out CFCombatantComponent? combat))
            return;

        HeldStats(args.User, out var stats);
        var bonus = CFMelee.DamageBonus(abilities, stats, combat, false);
        var damage = (int) (CFMelee.RollStrikeDamage(_random, stats, bonus, false, 0) * CFEffectSystem.DamageMultiplier(args));
        var type = CompOrNull<CFExemplarComponent>(args.User)?.SparkType ?? "Spirit";
        _effects.DealDamage(args.User, target, damage, type, false);
    }

    [SubscribeLocalEvent(after: [typeof(CFStrikeActionSystem), typeof(CFEffectActionSystem)])]
    private void OnShift(Entity<CFShiftImmanenceActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || !TryComp(args.Performer, out CFExemplarComponent? comp))
            return;

        // Shifting to where the spark already is sends it back to the soul.
        var index = comp.Ikons.IndexOf(ent.Comp.Ikon);
        var leaving = index == comp.Spark;
        SetSpark(args.Performer, comp, leaving ? -1 : index);
        Refresh(args.Performer, comp);

        var name = _prototypes.TryIndex(ent.Comp.Ikon, out var ikon) ? Loc.GetString(ikon.Name) : string.Empty;
        _popup.PopupEntity(Loc.GetString(leaving ? "cf-ikon-soul" : "cf-ikon-shift", ("ikon", name)), args.Performer, args.Performer);
    }

    [SubscribeLocalEvent(after: [typeof(CFStrikeActionSystem), typeof(CFEffectActionSystem)])]
    private void OnTranscendInstant(Entity<CFIkonTranscendenceComponent> ent, ref CFActivityInstantEvent args)
    {
        if (ent.Comp.Area && !args.Handled)
        {
            args.Handled = true;
            if (_net.IsServer)
                _effects.ResolveArea(ent, args.Performer, _transform.GetMapCoordinates(args.Performer));
        }

        if (args.Handled)
            Eject(args.Performer);
    }

    [SubscribeLocalEvent(after: [typeof(CFStrikeActionSystem), typeof(CFEffectActionSystem)])]
    private void OnTranscendTarget(Entity<CFIkonTranscendenceComponent> ent, ref CFActivityTargetEvent args)
    {
        if (ent.Comp.Area && !args.Handled)
        {
            args.Handled = true;
            if (_net.IsServer)
                _effects.ResolveArea(ent, args.Performer, _transform.GetMapCoordinates(args.Target));
        }

        if (args.Handled)
            Eject(args.Performer);
    }

    /// <summary>The spark leaves the ikon after a transcendence, into the next one. The rules let the Exemplar choose: real time has no pause to ask in.</summary>
    private void Eject(EntityUid user)
    {
        if (!_net.IsServer || !TryComp(user, out CFExemplarComponent? comp) || comp.Ikons.Count == 0)
            return;

        comp.LastTranscendence = _timing.CurTime;
        SetSpark(user, comp, (Math.Max(comp.Spark, 0) + 1) % comp.Ikons.Count);
        Refresh(user, comp);
        RunEpithet(user, comp);
    }

    private void RunEpithet(EntityUid user, CFExemplarComponent comp)
    {
        switch (comp.Epithet)
        {
            case CFEpithet.Brave:
                // Stands in for the free Stride, as tactics do.
                _bonuses.AddBonus(user, CFBonusStat.Speed, CFBonusType.Status, 10, 3f);
                break;
            case CFEpithet.Radiant:
                InspireAlly(user);
                break;
        }
    }

    private void InspireAlly(EntityUid user)
    {
        var center = _transform.GetMapCoordinates(user);
        var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
        var allies = _effects.CollectArea(user, CFAreaShape.Emanation, 30, center, center, atPoint: true, includeCenter: true, selfAllowed: false, CFTargetFilter.Allies);
        foreach (var ally in allies)
        {
            if (CompOrNull<CFCombatantComponent>(ally) is not { } combat || combat.CurrentHp >= combat.MaxHp)
                continue;

            if (CompOrNull<CFRadiantHealedComponent>(ally) is { } healed && _timing.CurTime < healed.Until)
                continue;

            EnsureComp<CFRadiantHealedComponent>(ally).Until = _timing.CurTime + TimeSpan.FromMinutes(10);
            _effects.Heal(user, ally, 2 + 2 * level);
            return;
        }
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFExemplarComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextCheck)
                continue;

            comp.NextCheck = now + CheckEvery;
            Refresh(uid, comp);
        }
    }

    /// <summary>Keeps the resistance topped up, and enables only the transcendence that can be used now.</summary>
    private void Refresh(EntityUid uid, CFExemplarComponent comp)
    {
        var now = _timing.CurTime;
        var current = Empowered(uid, comp);
        if (current?.Resistance is { } type)
        {
            var level = CompOrNull<CFCombatantComponent>(uid)?.Level ?? 1;
            _bonuses.AddResistance(uid, type, Math.Max(1, level / 2), weakness: false, 2f);
        }

        var ready = current != null && IsReady(uid, current);
        var spent = comp.LastTranscendence != TimeSpan.Zero && now - comp.LastTranscendence < Round;
        var recent = comp.LastHitAt != TimeSpan.Zero && now - comp.LastHitAt <= HitStaysRecent;
        foreach (var action in _actions.GetActions(uid))
        {
            if (!TryComp(action, out CFIkonTranscendenceComponent? transcendence))
                continue;

            var enabled = ready && !spent && current!.ID == transcendence.Ikon.Id && (!transcendence.NeedsLastHit || recent);
            if (action.Comp.Enabled != enabled)
                _actions.SetEnabled(action.AsNullable(), enabled);
        }
    }
}
