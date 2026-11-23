using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

public enum CFRuneUsage : byte
{
    Creature,
    Weapon,
    Shield,
}

/// <summary>A Runesmith rune. Its invocation is the <c>CFCheck</c> and <c>CFDegreeEffects</c> on its <see cref="Action"/>.</summary>
[Prototype("cfRune")]
public sealed partial class CFRunePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>The Trace action, which holds what invoking the rune does.</summary>
    [DataField(required: true)]
    public EntProtoId Action;

    [DataField]
    public CFRuneUsage Usage;

    /// <summary>A weapon must deal one of these. Empty means any.</summary>
    [DataField]
    public List<string> DamageTypes = new();

    /// <summary>Persistent bleed per weapon die on a Strike.</summary>
    [DataField]
    public int PersistentPerDie;

    /// <summary>Splash of the weapon's own damage type per weapon die on a Strike.</summary>
    [DataField]
    public int SplashPerDie;

    /// <summary>What the bearer has while the rune is on it.</summary>
    [DataField]
    public List<CFIkonBonus> Bonuses = new();

    /// <summary>The invocation goes at a creature next to the bearer, not the bearer.</summary>
    [DataField]
    public bool InvokesAdjacent;
}

public enum CFRuneItemKind : byte
{
    WeaponPotency,
    Striking,
    Property,
    ArmorPotency,
    Resilient,
    Runesmith,
}

/// <summary>What a rune recipe attaches. It is on the recipe's result, so the recipe's level comes from the same place as any other.</summary>
[RegisterComponent]
public sealed partial class CFRuneItemComponent : Component
{
    [DataField(required: true)]
    public CFRuneItemKind Kind;

    /// <summary>The rune's grade, such as +1 potency.</summary>
    [DataField]
    public int Value = 1;

    [DataField]
    public ProtoId<CFPropertyRunePrototype>? Property;

    [DataField]
    public ProtoId<CFRunePrototype>? Rune;
}

[DataDefinition]
public sealed partial class CFAppliedRune
{
    [DataField(required: true)]
    public ProtoId<CFRunePrototype> Rune;

    [DataField]
    public EntityUid Caster;

    /// <summary>Zero for an etched rune, which lasts until it is invoked.</summary>
    [DataField]
    public TimeSpan Expires;

    /// <summary>A Runic Tattoo: etched for good, outside the limit, and faded until the next day once invoked.</summary>
    [DataField]
    public bool Tattoo;
}

/// <summary>Runesmith runes on a creature or an item.</summary>
[RegisterComponent]
public sealed partial class CFRuneBearerComponent : Component
{
    [DataField]
    public List<CFAppliedRune> Runes = new();

    [DataField]
    public TimeSpan NextCheck;
}

/// <summary>The items a Runesmith has etched, oldest first, and what its feats change.</summary>
[RegisterComponent]
public sealed partial class CFRunesmithComponent : Component
{
    [DataField]
    public List<EntityUid> Etched = new();

    /// <summary>Rune-Singer: once per 10 minutes, a rune traced from afar costs 1 action, not 2.</summary>
    [DataField]
    public bool RuneSinger;

    [DataField]
    public TimeSpan NextSong;

    /// <summary>Smithing Weapons: a hammer, knife or pick deals 1 fire more to an enemy bearing one of the Runesmith's runes.</summary>
    [DataField]
    public bool SmithingWeapons;

    /// <summary>Runic Tattoo: the rune on the Runesmith's own body, or null.</summary>
    [DataField]
    public ProtoId<CFRunePrototype>? Tattoo;

    /// <summary>The tattoo has been invoked today and waits for a long rest.</summary>
    [DataField]
    public bool TattooFaded;
}

/// <summary>An action that traces a rune on a creature.</summary>
[RegisterComponent]
public sealed partial class CFTraceRuneActionComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CFRunePrototype> Rune;
}

/// <summary>An action that invokes the user's runes on a creature, or on what it is holding.</summary>
[RegisterComponent]
public sealed partial class CFInvokeRuneActionComponent : Component
{
}

/// <summary>Extra weapon dice on the bearer's next Strike to hit.</summary>
[RegisterComponent]
public sealed partial class CFRuneNextStrikeComponent : Component
{
    [DataField]
    public int Dice;

    [DataField]
    public bool UnarmedOnly;

    [DataField]
    public TimeSpan Until;
}

/// <summary>The target's next Strike to hit deals another weapon die.</summary>
public sealed partial class CFRuneNextStrikeEffect : CFEffect
{
    [DataField]
    public int Dice = 1;

    [DataField]
    public bool UnarmedOnly;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFRuneSystem>().PrimeNextStrike(target, Dice, UnarmedOnly);
    }
}

/// <summary>
/// Traces one of the user's runes on the target, or on the shield the user holds. The rune is the one at <see cref="Runes"/>
/// index mode - 1 when the action has a <c>CFModeChoice</c>, else the first. A rune the user does not know does nothing.
/// </summary>
public sealed partial class CFTraceRuneEffect : CFEffect
{
    [DataField(required: true)]
    public List<ProtoId<CFRunePrototype>> Runes = new();

    /// <summary>The rune goes on the shield in the user's hand (Fortifying Knock).</summary>
    [DataField]
    public bool OnShield;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (Runes.Count == 0)
            return;

        var index = args.Mode > 0 ? Math.Min(args.Mode - 1, Runes.Count - 1) : 0;
        var runes = entities.System<CFRuneSystem>();
        EntityUid? target = args.Target;
        if (OnShield)
            target = entities.System<CFShieldSystem>().TryGetUsableShield(args.User, out var shield) ? shield : null;

        if (target is { } bearer)
            runes.TryTraceKnown(args.User, bearer, Runes[index]);
    }
}

/// <summary>Invokes the user's runes on the target, as Invoke Rune does (Remote Detonation, Terrifying Invocation).</summary>
public sealed partial class CFInvokeRunesEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFRuneSystem>().Invoke(args.User, target);
    }
}

/// <summary>Song of Glorious Invocation: one rune on each of up to three bearers within range, who also take heart.</summary>
public sealed partial class CFInvokeAllEffect : CFEffect
{
    [DataField]
    public int Feet = 30;

    [DataField]
    public int Bearers = 3;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFRuneSystem>().InvokeAround(args.User, Feet, Bearers);
    }
}

/// <summary>Transpose Etching: the user's newest rune within range moves onto the target.</summary>
public sealed partial class CFTransposeRuneEffect : CFEffect
{
    [DataField]
    public int Feet = 30;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFRuneSystem>().Transpose(args.User, target, Feet);
    }
}

/// <summary>Demoralizes the target at no cost, as part of another action (Terrifying Invocation).</summary>
public sealed partial class CFDemoralizeEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target && target != args.User)
            entities.System<CFConditionSystem>().TryDemoralize(args.User, target);
    }
}

/// <summary>
/// Zohk: the target is called to the user's side. An ally comes at once; an enemy only on a failed save, so put the
/// action's check on it and this in the <c>effects</c> list.
/// </summary>
public sealed partial class CFTeleportToUserEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || target == args.User)
            return;

        var enemy = entities.System<Content.Shared.CF.Factions.CFFactionSystem>().CanHarm(args.User, target);
        if (enemy && args.Degree is not (Content.Shared.CF.Checks.CFDegree.Failure or Content.Shared.CF.Checks.CFDegree.CriticalFailure))
            return;

        var transform = entities.System<SharedTransformSystem>();
        var coords = entities.GetComponent<TransformComponent>(args.User).Coordinates;
        var direction = transform.GetWorldPosition(target) - transform.GetWorldPosition(args.User);
        var offset = direction.LengthSquared() > 0.01f ? System.Numerics.Vector2.Normalize(direction) : System.Numerics.Vector2.UnitX;
        transform.SetCoordinates(target, coords.Offset(offset));
    }
}

/// <summary>The target raises the shield it is holding.</summary>
public sealed partial class CFRaiseShieldEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var shields = entities.System<CFShieldSystem>();
        if (shields.TryGetUsableShield(target, out var shield))
            shields.SetRaised(shield, true);
    }
}

/// <summary>Attaching item runes and etched runes at a workbench, and tracing and invoking a Runesmith's runes.</summary>
public sealed partial class CFRuneSystem : EntitySystem
{
    private const float AdjacentTiles = 1.5f;
    private static readonly TimeSpan SongWait = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan TraceLasts = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan NextStrikeLasts = TimeSpan.FromSeconds(12);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _pool = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFIkonSystem _ikons = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;

    private static string Source(ProtoId<CFRunePrototype> rune) => "rune:" + rune.Id;

    /// <summary>The first thing on a workbench this rune can go on, or null.</summary>
    public EntityUid? FindTarget(HashSet<EntityUid> placed, EntProtoId rune)
    {
        if (!_prototypes.Index(rune).TryComp(out CFRuneItemComponent? item, Factory))
            return null;

        foreach (var candidate in placed)
        {
            if (CanAttach(candidate, item))
                return candidate;
        }

        return null;
    }

    public bool CanAttach(EntityUid target, CFRuneItemComponent item)
    {
        var runes = CompOrNull<CFWeaponRunesComponent>(target);
        var armor = CompOrNull<CFArmorRunesComponent>(target);
        switch (item.Kind)
        {
            case CFRuneItemKind.WeaponPotency:
                return HasComp<CFWeaponComponent>(target) && (runes?.Potency ?? 0) < item.Value;
            case CFRuneItemKind.Striking:
                return HasComp<CFWeaponComponent>(target) && (runes?.Striking ?? 0) < item.Value;
            case CFRuneItemKind.Property:
                return HasComp<CFWeaponComponent>(target)
                       && runes != null
                       && item.Property is { } property
                       && runes.Property.Count < runes.Potency
                       && !runes.Property.Contains(property);
            case CFRuneItemKind.ArmorPotency:
                return HasComp<CFArmorComponent>(target) && (armor?.Potency ?? 0) < item.Value;
            case CFRuneItemKind.Resilient:
                return HasComp<CFArmorComponent>(target) && (armor?.Resilient ?? 0) < item.Value;
            case CFRuneItemKind.Runesmith:
                return item.Rune is { } rune && CanEtch(target, rune);
            default:
                return false;
        }
    }

    private bool CanEtch(EntityUid target, ProtoId<CFRunePrototype> id)
    {
        if (!_prototypes.TryIndex(id, out var rune))
            return false;

        if (CompOrNull<CFRuneBearerComponent>(target)?.Runes.Exists(applied => applied.Rune == id) == true)
            return false;

        return rune.Usage switch
        {
            CFRuneUsage.Weapon => _weapons.TryStats(target, out var stats) && FitsDamage(rune, stats),
            CFRuneUsage.Shield => HasComp<CFShieldComponent>(target),
            _ => false,
        };
    }

    private static bool FitsDamage(CFRunePrototype rune, CFStrikeWeaponStats stats)
    {
        return rune.DamageTypes.Count == 0
               || rune.DamageTypes.Exists(type => type.Equals(stats.DamageType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Anyone can attach an item rune, but only a Runesmith who knows it can etch a Runesmith rune.</summary>
    public bool CanUseRecipe(EntityUid user, EntProtoId rune)
    {
        if (!_prototypes.Index(rune).TryComp(out CFRuneItemComponent? item, Factory) || item.Kind != CFRuneItemKind.Runesmith)
            return true;

        return item.Rune is { } known && TraceAction(user, known) != null;
    }

    /// <summary>Puts a rune recipe's rune on the target.</summary>
    public void Attach(EntityUid target, EntProtoId rune, EntityUid crafter)
    {
        if (!_prototypes.Index(rune).TryComp(out CFRuneItemComponent? item, Factory))
            return;

        switch (item.Kind)
        {
            case CFRuneItemKind.WeaponPotency:
                EnsureComp<CFWeaponRunesComponent>(target).Potency = item.Value;
                break;
            case CFRuneItemKind.Striking:
                EnsureComp<CFWeaponRunesComponent>(target).Striking = item.Value;
                break;
            case CFRuneItemKind.Property when item.Property is { } property:
                EnsureComp<CFWeaponRunesComponent>(target).Property.Add(property);
                break;
            case CFRuneItemKind.ArmorPotency:
                EnsureComp<CFArmorRunesComponent>(target).Potency = item.Value;
                break;
            case CFRuneItemKind.Resilient:
                EnsureComp<CFArmorRunesComponent>(target).Resilient = item.Value;
                break;
            case CFRuneItemKind.Runesmith when item.Rune is { } etched:
                Etch(target, etched, crafter);
                return;
        }

        if (TryComp(target, out CFWeaponRunesComponent? weapon))
            Dirty(target, weapon);

        if (TryComp(target, out CFArmorRunesComponent? armor))
            Dirty(target, armor);
    }

    /// <summary>Two etched runes at a time, three from level 5. A new one past that makes the oldest fade.</summary>
    /// <summary>Runic Tattoo: the rune is etched on the Runesmith for good, outside the etched limit.</summary>
    public void SetTattoo(EntityUid caster, ProtoId<CFRunePrototype> rune)
    {
        var smith = EnsureComp<CFRunesmithComponent>(caster);
        smith.Tattoo = rune;
        smith.TattooFaded = false;
        RestoreTattoo(caster);
    }

    /// <summary>Puts a faded tattoo back, as a long rest does.</summary>
    public void RestoreTattoo(EntityUid caster)
    {
        if (!TryComp(caster, out CFRunesmithComponent? smith) || smith.Tattoo is not { } rune || !_prototypes.TryIndex(rune, out var proto))
            return;

        smith.TattooFaded = false;
        var bearer = EnsureComp<CFRuneBearerComponent>(caster);
        if (bearer.Runes.Exists(applied => applied.Tattoo))
            return;

        bearer.Runes.Add(new CFAppliedRune { Rune = rune, Caster = caster, Tattoo = true });
        _bonuses.RemoveBonusesFrom(caster, Source(rune));
        foreach (var bonus in proto.Bonuses)
        {
            _bonuses.AddPermanentBonus(caster, bonus.Stat, bonus.Type, bonus.Value, bonus.Trait, bonus.Skill, Source(rune));
        }
    }

    public void SetRuneSinger(EntityUid caster)
    {
        EnsureComp<CFRunesmithComponent>(caster).RuneSinger = true;
    }

    public void SetSmithingWeapons(EntityUid caster)
    {
        EnsureComp<CFRunesmithComponent>(caster).SmithingWeapons = true;
    }

    /// <summary>Traces a rune the caster knows on a creature or item that can bear it. False if it knows no such rune or the target cannot.</summary>
    public bool TryTraceKnown(EntityUid caster, EntityUid target, ProtoId<CFRunePrototype> id)
    {
        if (!_net.IsServer || !_prototypes.TryIndex(id, out var rune))
            return false;

        if (TraceAction(caster, id) == null)
        {
            _popup.PopupEntity(Loc.GetString("cf-rune-unknown"), caster, caster);
            return false;
        }

        var fits = HasComp<MobStateComponent>(target) ? CanBear(target, rune) : CanEtch(target, id) || rune.Usage == CFRuneUsage.Shield && HasComp<CFShieldComponent>(target);
        if (!fits)
        {
            _popup.PopupEntity(Loc.GetString("cf-rune-cannot-bear"), caster, caster);
            return false;
        }

        Trace(target, rune, caster);
        return true;
    }

    /// <summary>Song of Glorious Invocation: one rune on each of up to <paramref name="bearers"/> creatures within range, newest first, and heart for each.</summary>
    public int InvokeAround(EntityUid caster, int feet, int bearers)
    {
        if (!_net.IsServer)
            return 0;

        var count = 0;
        var center = _transform.GetMapCoordinates(caster);
        var found = _effects.CollectArea(caster, CFAreaShape.Emanation, feet, center, center, atPoint: true, includeCenter: true, selfAllowed: true, CFTargetFilter.All);
        foreach (var creature in found)
        {
            if (count >= bearers)
                break;

            var invoked = InvokeOn(caster, creature, creature, 1);
            if (invoked == 0)
            {
                foreach (var held in _hands.EnumerateHeld(creature))
                {
                    invoked = InvokeOn(caster, held, creature, 1);
                    if (invoked > 0)
                        break;
                }
            }

            if (invoked == 0)
                continue;

            count++;
            _bonuses.AddBonus(creature, CFBonusStat.SkillChecks, CFBonusType.Status, 1, 60f, source: "glorious-invocation");
            _bonuses.AddBonus(creature, CFBonusStat.Saves, CFBonusType.Status, 1, 60f, "Fear", source: "glorious-invocation");
        }

        return count;
    }

    /// <summary>Transpose Etching: the caster's newest rune on anyone or anything within range moves onto the target.</summary>
    public bool Transpose(EntityUid caster, EntityUid target, int feet)
    {
        if (!_net.IsServer)
            return false;

        var center = _transform.GetMapCoordinates(caster);
        var reach = feet / CFMelee.FeetPerTile + 0.5f;
        CFRuneBearerComponent? from = null;
        var index = -1;
        var query = EntityQueryEnumerator<CFRuneBearerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var bearer, out var xform))
        {
            if (uid == target || xform.MapID != center.MapId || (_transform.GetWorldPosition(xform) - center.Position).Length() > reach)
                continue;

            for (var i = bearer.Runes.Count - 1; i >= 0; i--)
            {
                if (bearer.Runes[i].Caster == caster && !bearer.Runes[i].Tattoo)
                {
                    from = bearer;
                    index = i;
                    break;
                }
            }

            if (from != null)
                break;
        }

        if (from == null || !_prototypes.TryIndex(from.Runes[index].Rune, out var rune))
        {
            _popup.PopupEntity(Loc.GetString("cf-rune-none-near"), caster, caster);
            return false;
        }

        var fits = HasComp<MobStateComponent>(target) ? CanBear(target, rune) : CanEtch(target, rune.ID) || rune.Usage == CFRuneUsage.Shield && HasComp<CFShieldComponent>(target);
        if (!fits)
        {
            _popup.PopupEntity(Loc.GetString("cf-rune-cannot-bear"), caster, caster);
            return false;
        }

        var moved = from.Runes[index];
        from.Runes.RemoveAt(index);
        _bonuses.RemoveBonusesFrom(from.Owner, Source(rune.ID));
        if (moved.Expires == TimeSpan.Zero && TryComp(caster, out CFRunesmithComponent? smith))
        {
            smith.Etched.Remove(from.Owner);
            smith.Etched.Add(target);
        }

        var to = EnsureComp<CFRuneBearerComponent>(target);
        to.Runes.Add(moved);
        _bonuses.RemoveBonusesFrom(target, Source(rune.ID));
        foreach (var bonus in rune.Bonuses)
        {
            _bonuses.AddPermanentBonus(target, bonus.Stat, bonus.Type, bonus.Value, bonus.Trait, bonus.Skill, Source(rune.ID));
        }

        return true;
    }

    public void Etch(EntityUid target, ProtoId<CFRunePrototype> rune, EntityUid caster)
    {
        var bearer = EnsureComp<CFRuneBearerComponent>(target);
        bearer.Runes.Add(new CFAppliedRune { Rune = rune, Caster = caster });

        var smith = EnsureComp<CFRunesmithComponent>(caster);
        smith.Etched.Add(target);

        var level = CompOrNull<CFCombatantComponent>(caster)?.Level ?? 1;
        var most = level >= 5 ? 3 : 2;
        while (smith.Etched.Count > most)
        {
            var oldest = smith.Etched[0];
            smith.Etched.RemoveAt(0);
            if (!TryComp(oldest, out CFRuneBearerComponent? old))
                continue;

            var index = old.Runes.FindIndex(applied => applied.Expires == TimeSpan.Zero && applied.Caster == caster);
            if (index >= 0)
                old.Runes.RemoveAt(index);
        }
    }

    public void PrimeNextStrike(EntityUid target, int dice, bool unarmedOnly)
    {
        var next = EnsureComp<CFRuneNextStrikeComponent>(target);
        next.Dice = dice;
        next.UnarmedOnly = unarmedOnly;
        next.Until = _timing.CurTime + NextStrikeLasts;
    }

    /// <summary>Extra weapon dice for the Strike about to be rolled. Only a hit uses them up, in <see cref="NoteHit"/>.</summary>
    public int NextStrikeDice(EntityUid user, CFStrikeWeaponStats weapon)
    {
        if (!TryComp(user, out CFRuneNextStrikeComponent? next) || _timing.CurTime >= next.Until)
            return 0;

        return next.UnarmedOnly && !weapon.Traits.Unarmed ? 0 : next.Dice;
    }

    public void NoteHit(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, EntityUid weaponUid)
    {
        if (!_net.IsServer)
            return;

        if (NextStrikeDice(user, weapon) > 0)
            RemCompDeferred<CFRuneNextStrikeComponent>(user);

        OnStrikeFrom(user, target, weapon, user);
        if (weaponUid != user)
            OnStrikeFrom(user, target, weapon, weaponUid);

        // Smithing Weapons: sparks fly from a hammer, knife or pick on an enemy that bears one of the Runesmith's runes.
        if (CompOrNull<CFRunesmithComponent>(user) is { SmithingWeapons: true }
            && CompOrNull<CFWeaponComponent>(weaponUid) is { Group: CFWeaponGroup.Hammer or CFWeaponGroup.Knife or CFWeaponGroup.Pick }
            && CompOrNull<CFRuneBearerComponent>(target)?.Runes.Exists(applied => applied.Caster == user) == true)
        {
            _effects.DealDamage(user, target, 1, "Fire", false);
        }
    }

    private void OnStrikeFrom(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, EntityUid bearer)
    {
        if (!TryComp(bearer, out CFRuneBearerComponent? runes))
            return;

        foreach (var applied in runes.Runes)
        {
            if (!_prototypes.TryIndex(applied.Rune, out var rune) || rune.Usage != CFRuneUsage.Weapon || !FitsDamage(rune, weapon))
                continue;

            if (rune.PersistentPerDie > 0)
                _bonuses.AddPersistent(target, "Bleed", 0, 6, rune.PersistentPerDie * weapon.DamageDice, user);

            if (rune.SplashPerDie > 0)
                Splash(user, target, weapon, rune.SplashPerDie * weapon.DamageDice);
        }
    }

    private void Splash(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, int amount)
    {
        var center = _transform.GetMapCoordinates(target);
        var found = _effects.CollectArea(user, CFAreaShape.Burst, 5, center, center, atPoint: true, includeCenter: true, selfAllowed: false, CFTargetFilter.Enemies);
        foreach (var creature in found)
        {
            _effects.DealDamage(user, creature, amount, weapon.DamageType, false);
        }
    }

    private bool CanBear(EntityUid target, CFRunePrototype rune)
    {
        switch (rune.Usage)
        {
            case CFRuneUsage.Weapon:
                _ikons.HeldStats(target, out var stats);
                return FitsDamage(rune, stats);
            case CFRuneUsage.Shield:
                return _shields.TryGetUsableShield(target, out _);
            default:
                return HasComp<MobStateComponent>(target);
        }
    }

    [SubscribeLocalEvent]
    private void OnTrace(Entity<CFTraceRuneActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled || !_prototypes.TryIndex(ent.Comp.Rune, out var rune))
            return;

        // Not handled means the action costs nothing, which is right for one that could not be used.
        if (!CanBear(args.Target, rune))
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-rune-cannot-bear"), args.Performer, args.Performer);

            return;
        }

        // The wheel has already charged 1 action. Tracing from further away than next to the target is 2, unless a
        // Rune-Singer sings it, once per 10 minutes.
        var user = _transform.GetWorldPosition(args.Performer);
        var far = (user - _transform.GetWorldPosition(args.Target)).Length() > AdjacentTiles;
        if (_net.IsServer && far)
        {
            var smith = CompOrNull<CFRunesmithComponent>(args.Performer);
            if (smith is { RuneSinger: true } && _timing.CurTime >= smith.NextSong)
                smith.NextSong = _timing.CurTime + SongWait;
            else if (!_pool.TrySpendActions(args.Performer, 1))
            {
                _popup.PopupEntity(Loc.GetString("cf-rune-too-far"), args.Performer, args.Performer);
                return;
            }
        }

        args.Handled = true;
        if (_net.IsServer)
            Trace(args.Target, rune, args.Performer);
    }

    public void Trace(EntityUid target, CFRunePrototype rune, EntityUid caster)
    {
        var bearer = EnsureComp<CFRuneBearerComponent>(target);
        var expires = _timing.CurTime + TraceLasts;
        var existing = bearer.Runes.Find(applied => applied.Rune.Id == rune.ID && applied.Caster == caster && applied.Expires != TimeSpan.Zero);
        if (existing != null)
            existing.Expires = expires;
        else
            bearer.Runes.Add(new CFAppliedRune { Rune = rune.ID, Caster = caster, Expires = expires });

        _bonuses.RemoveBonusesFrom(target, Source(rune.ID));
        foreach (var bonus in rune.Bonuses)
        {
            _bonuses.AddPermanentBonus(target, bonus.Stat, bonus.Type, bonus.Value, bonus.Trait, bonus.Skill, Source(rune.ID));
        }
    }

    [SubscribeLocalEvent]
    private void OnInvoke(Entity<CFInvokeRuneActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        if (Invoke(args.Performer, args.Target) == 0)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-rune-none-here"), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
    }

    /// <summary>Invokes up to two of the caster's runes on a creature and what it holds, newest first. Returns how many.</summary>
    public int Invoke(EntityUid caster, EntityUid creature)
    {
        var count = InvokeOn(caster, creature, creature, 2);
        foreach (var held in _hands.EnumerateHeld(creature))
        {
            if (count >= 2)
                break;

            count += InvokeOn(caster, held, creature, 2 - count);
        }

        return count;
    }

    private int InvokeOn(EntityUid caster, EntityUid bearerUid, EntityUid creature, int most)
    {
        if (!TryComp(bearerUid, out CFRuneBearerComponent? bearer))
            return 0;

        var count = 0;
        for (var i = bearer.Runes.Count - 1; i >= 0 && count < most; i--)
        {
            var applied = bearer.Runes[i];
            if (applied.Caster != caster)
                continue;

            // A faded tattoo waits for the next day.
            if (applied.Tattoo && CompOrNull<CFRunesmithComponent>(caster) is { TattooFaded: true })
                continue;

            bearer.Runes.RemoveAt(i);
            count++;
            if (!_net.IsServer)
                continue;

            _bonuses.RemoveBonusesFrom(bearerUid, Source(applied.Rune));
            if (applied.Tattoo && TryComp(caster, out CFRunesmithComponent? tattooed))
                tattooed.TattooFaded = true;
            else if (applied.Expires == TimeSpan.Zero && TryComp(caster, out CFRunesmithComponent? smith))
                smith.Etched.Remove(bearerUid);

            Resolve(caster, creature, applied.Rune);
        }

        return count;
    }

    private void Resolve(EntityUid caster, EntityUid creature, ProtoId<CFRunePrototype> id)
    {
        if (!_prototypes.TryIndex(id, out var rune) || TraceAction(caster, id) is not { } action || !HasComp<CFDegreeEffectsComponent>(action))
            return;

        // An invocation with an area reaches around the bearer (Baruiel, Ledria, Pluuna).
        if (HasComp<CFAreaEffectComponent>(action))
        {
            _effects.ResolveArea(action, caster, _transform.GetMapCoordinates(creature));
            return;
        }

        var victim = creature;
        if (rune.InvokesAdjacent)
        {
            var center = _transform.GetMapCoordinates(creature);
            var found = _effects.CollectArea(caster, CFAreaShape.Emanation, 5, center, center, atPoint: true, includeCenter: false, selfAllowed: false, CFTargetFilter.Enemies, null, 1);
            if (found.Count == 0)
                return;

            victim = found[0];
        }

        _effects.ResolveTarget(action, caster, victim);
    }

    private EntityUid? TraceAction(EntityUid caster, ProtoId<CFRunePrototype> rune)
    {
        foreach (var action in _actions.GetActions(caster))
        {
            if (TryComp(action, out CFTraceRuneActionComponent? trace) && trace.Rune == rune)
                return action;
        }

        return null;
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFRuneBearerComponent>();
        while (query.MoveNext(out var uid, out var bearer))
        {
            if (now < bearer.NextCheck)
                continue;

            bearer.NextCheck = now + CheckEvery;
            for (var i = bearer.Runes.Count - 1; i >= 0; i--)
            {
                var applied = bearer.Runes[i];
                if (applied.Expires == TimeSpan.Zero || now < applied.Expires)
                    continue;

                bearer.Runes.RemoveAt(i);
                _bonuses.RemoveBonusesFrom(uid, Source(applied.Rune));
            }
        }
    }
}

/// <summary>A workbench must hold something this rune can go on. It is never used up, so <c>count</c> should be 0.</summary>
public sealed partial class CFRuneTargetRequirement : CFCraftRequirement
{
    [DataField(required: true)]
    public EntProtoId Rune;

    public override bool IsMet(IEntityManager entities, HashSet<EntityUid> placed)
    {
        return entities.System<CFRuneSystem>().FindTarget(placed, Rune) != null;
    }

    public override void Consume(IEntityManager entities, HashSet<EntityUid> placed, int units)
    {
    }

    public override string Title(IPrototypeManager prototypes)
    {
        return Loc.GetString("cf-rune-needs-target");
    }
}
