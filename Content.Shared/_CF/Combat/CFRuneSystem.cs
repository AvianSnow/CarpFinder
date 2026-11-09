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

/// <summary>The items a Runesmith has etched, oldest first.</summary>
[RegisterComponent]
public sealed partial class CFRunesmithComponent : Component
{
    [DataField]
    public List<EntityUid> Etched = new();
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

        // The wheel has already charged 1 action. Tracing from further away than next to the target is 2.
        var user = _transform.GetWorldPosition(args.Performer);
        var far = (user - _transform.GetWorldPosition(args.Target)).Length() > AdjacentTiles;
        if (_net.IsServer && far && !_pool.TrySpendActions(args.Performer, 1))
        {
            _popup.PopupEntity(Loc.GetString("cf-rune-too-far"), args.Performer, args.Performer);
            return;
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

            bearer.Runes.RemoveAt(i);
            count++;
            if (!_net.IsServer)
                continue;

            _bonuses.RemoveBonusesFrom(bearerUid, Source(applied.Rune));
            if (applied.Expires == TimeSpan.Zero && TryComp(caster, out CFRunesmithComponent? smith))
                smith.Etched.Remove(bearerUid);

            Resolve(caster, creature, applied.Rune);
        }

        return count;
    }

    private void Resolve(EntityUid caster, EntityUid creature, ProtoId<CFRunePrototype> id)
    {
        if (!_prototypes.TryIndex(id, out var rune) || TraceAction(caster, id) is not { } action || !HasComp<CFDegreeEffectsComponent>(action))
            return;

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
