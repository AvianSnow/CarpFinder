using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Companions;

/// <summary>A Summoner's bond with their eidolon: what the eidolon is, and the body it has while manifested.</summary>
[RegisterComponent]
public sealed partial class CFEidolonLinkComponent : Component
{
    /// <summary>The manifested body. Invalid while it is not manifested.</summary>
    [DataField]
    public EntityUid Eidolon;

    /// <summary>The shared Hit Points as they stood the last time the two bodies were matched.</summary>
    [DataField]
    public int SharedHp;

    [DataField]
    public string Name = "eidolon";

    [DataField]
    public int Constitution;

    [DataField]
    public int Intelligence;

    [DataField]
    public int Wisdom;

    [DataField]
    public int Charisma;

    [DataField]
    public int SpeedFeet = 25;

    /// <summary>True when Dexterity is the eidolon's key attribute, not Strength.</summary>
    [DataField]
    public bool DexterityKey;

    [DataField]
    public string AttackType = "bludgeoning";

    /// <summary>Resistance of half the eidolon's level.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? Resistance;

    /// <summary>The eidolon heals from void and is hurt by vitality.</summary>
    [DataField]
    public bool VoidHealing;

    /// <summary>Feet added to the eidolon's Speed by feats.</summary>
    [DataField]
    public int ExtraSpeed;

    [DataField]
    public TimeSpan NextTether;

    /// <summary>A second damage type the attack can deal (Advanced Weaponry, Dual Energy Heart, Demonic Strikes). Empty is none.</summary>
    [DataField]
    public string Versatile = string.Empty;

    /// <summary>Expanded Senses: darkvision and scent.</summary>
    [DataField]
    public bool Senses;

    /// <summary>Ranged Combatant: the damage type of a 1d4 ranged attack with a 30-foot increment. Empty is none.</summary>
    [DataField]
    public string RangedType = string.Empty;

    /// <summary>Bloodletting Claws: a critical hit with a slashing or piercing attack deals 1d6 persistent bleed.</summary>
    [DataField]
    public bool BloodlettingClaws;

    /// <summary>Reactive Dismissal: a reaction on the eidolon that takes double the level off damage and unmanifests it.</summary>
    [DataField]
    public bool ReactiveDismissal;

    /// <summary>Surprising Anatomy: the eidolon Strikes back at an adjacent enemy that damages it.</summary>
    [DataField]
    public bool StrikesBack;

    /// <summary>Hallowed or Demonic Strikes: 1 spirit damage more against a creature with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? SpiritAgainst;

    /// <summary>Spirit Touch: 1 void damage more to the living, 1 vitality more to the undead.</summary>
    [DataField]
    public bool SpiritTouch;

    /// <summary>Traits the manifested body has (Holy, Unholy).</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();

    /// <summary>Traits the eidolon has +2 circumstance to saves against (Construct Heart, Elemental Core, Void Essence).</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> SaveBonusTraits = new();

    /// <summary>Resistances of half the level, beside <see cref="Resistance"/> (Energy Heart).</summary>
    [DataField]
    public List<ProtoId<CFDamageTypePrototype>> Resistances = new();

    /// <summary>Unfetter Eidolon: until this time the tether does not pull it back.</summary>
    [DataField]
    public TimeSpan UnfetteredUntil;

    /// <summary>After a Reactive Dismissal, the eidolon cannot be manifested before this time.</summary>
    [DataField]
    public TimeSpan NextManifest;
}

/// <summary>On a manifested eidolon: whose it is.</summary>
[RegisterComponent]
public sealed partial class CFEidolonBodyComponent : Component
{
    [DataField]
    public EntityUid Summoner;
}

/// <summary>Regains Hit Points every 6 seconds for a while (Lifelink Surge).</summary>
[RegisterComponent]
public sealed partial class CFFastHealingComponent : Component
{
    [DataField]
    public int PerPulse;

    [DataField]
    public TimeSpan Until;

    [DataField]
    public TimeSpan Next;
}

/// <summary>Gives the target fast healing: <see cref="Amount"/> every 6 seconds, one more <see cref="HeightenAmount"/> per rank.</summary>
public sealed partial class CFFastHealingEffect : CFEffect
{
    [DataField]
    public int Amount = 4;

    [DataField]
    public int HeightenAmount = 2;

    [DataField]
    public float DurationSeconds = 24f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var target = args.Target ?? args.User;
        var now = entities.System<CFEidolonSystem>().Now;
        var healing = entities.EnsureComponent<CFFastHealingComponent>(target);
        healing.PerPulse = Math.Max(healing.PerPulse, Amount + HeightenAmount * args.HeightenSteps);
        healing.Until = now + TimeSpan.FromSeconds(DurationSeconds);
        if (healing.Next == TimeSpan.Zero || healing.Next < now)
            healing.Next = now + CFEidolonSystem.Pulse;
    }
}

/// <summary>Unfetter Eidolon: the tether lets go for a while.</summary>
public sealed partial class CFUnfetterEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (entities.TryGetComponent(args.User, out CFEidolonLinkComponent? link))
            link.UnfetteredUntil = entities.System<CFEidolonSystem>().Now + TimeSpan.FromSeconds(DurationSeconds);
    }
}

/// <summary>Raised on a manifested eidolon that must go at once (Reactive Dismissal). The server answers it.</summary>
[ByRefEvent]
public readonly record struct CFEidolonDismissEvent;

/// <summary>An action that manifests the eidolon, or unmanifests it if it is already here.</summary>
[RegisterComponent]
public sealed partial class CFManifestEidolonActionComponent : Component
{
}

/// <summary>An action where the eidolon Strikes the target, if it can reach.</summary>
[RegisterComponent]
public sealed partial class CFEidolonStrikeActionComponent : Component
{
    [DataField]
    public int ExtraDice;

    [DataField]
    public int BonusDamage;

    /// <summary>Feet of reach added for this Strike.</summary>
    [DataField]
    public int ExtraReachFeet;

    /// <summary>The eidolon's ranged attack (Ranged Combatant), with this range increment.</summary>
    [DataField]
    public bool Ranged;

    [DataField]
    public int RangeFeet = 30;

    /// <summary>The eidolon rushes up to this many feet at the target first (Beast's Charge).</summary>
    [DataField]
    public int ClosesFeet;

    /// <summary>A bonus to the attack roll when the rush was 20 feet or more.</summary>
    [DataField]
    public int BonusIfRushed;
}

/// <summary>Runs its effects with the caster's eidolon as the target. Does nothing while it is not manifested.</summary>
public sealed partial class CFOnEidolonEffect : CFEffect
{
    [DataField]
    public List<CFEffect> Effects = new();

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (!entities.TryGetComponent(args.User, out CFEidolonLinkComponent? link) || !link.Eidolon.IsValid())
            return;

        var target = args.Target;
        args.Target = link.Eidolon;
        entities.System<CFEffectSystem>().Run(Effects, args);
        args.Target = target;
    }
}

/// <summary>The parts of an eidolon that do not need the server's companion code.</summary>
public sealed partial class CFEidolonSystem : EntitySystem
{
    private const float BaseReachTiles = 1.5f;
    private const float FeetPerTile = 5f;

    /// <summary>How often fast healing pulses.</summary>
    public static readonly TimeSpan Pulse = TimeSpan.FromSeconds(6);

    /// <summary>How fast an eidolon covers the ground in a charge. Stock's jump speed.</summary>
    private const float RushSpeed = 10f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    public TimeSpan Now => _timing.CurTime;

    /// <summary>The link an eidolon body belongs to, or null.</summary>
    public CFEidolonLinkComponent? LinkOf(EntityUid eidolon)
    {
        return TryComp(eidolon, out CFEidolonBodyComponent? body) && TryComp(body.Summoner, out CFEidolonLinkComponent? link) ? link : null;
    }

    /// <summary>What an eidolon's Strike adds when it hits: Bloodletting Claws, Hallowed or Demonic Strikes, Spirit Touch.</summary>
    public void NoteHit(EntityUid user, EntityUid target, CFStrikeWeaponStats weapon, bool critical, int damage)
    {
        if (!_net.IsServer || damage <= 0 || LinkOf(user) is not { } link || TerminatingOrDeleted(target))
            return;

        if (link.BloodlettingClaws && critical && weapon.DamageType is "Slashing" or "Piercing")
            _bonuses.AddPersistent(target, "Bleed", 1, 6, 0, user);

        if (link.SpiritAgainst is { } trait && CompOrNull<CFTraitComponent>(target)?.HasTrait(trait) == true)
            _effects.DealDamage(user, target, 1, "Spirit", false);

        if (link.SpiritTouch)
            _effects.DealDamage(user, target, 1, CFVitalEnergies.IsLiving(EntityManager, target) ? "Void" : "Vitality", false);
    }

    public EntityUid? Manifested(EntityUid summoner)
    {
        return CompOrNull<CFEidolonLinkComponent>(summoner) is { } link && link.Eidolon.IsValid() && !TerminatingOrDeleted(link.Eidolon)
            ? link.Eidolon
            : null;
    }

    [SubscribeLocalEvent]
    private void OnEidolonStrike(Entity<CFEidolonStrikeActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        // Not handled means the action costs nothing, which is right for one that could not be used.
        if (Manifested(args.Performer) is not { } eidolon)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-eidolon-none"), args.Performer, args.Performer);

            return;
        }

        var reach = BaseReachTiles + ent.Comp.ExtraReachFeet / FeetPerTile + ent.Comp.ClosesFeet / FeetPerTile;
        if (ent.Comp.Ranged)
            reach = ent.Comp.RangeFeet / FeetPerTile;

        var distance = (_transform.GetWorldPosition(eidolon) - _transform.GetWorldPosition(args.Target)).Length();
        if (distance > reach)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-eidolon-out-of-reach"), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (ent.Comp.Ranged)
        {
            var type = LinkOf(eidolon)?.RangedType is { Length: > 0 } ranged ? ranged : "Bludgeoning";
            var shot = new CFStrikeWeaponStats(1, 4, type, "Unarmed", CFWeaponTraits.None, Ranged: true, RangeIncrementFeet: ent.Comp.RangeFeet);
            _strikes.PerformStrike(eidolon, args.Target, shot, eidolon, melee: false, extraDice: ent.Comp.ExtraDice, bonusDamage: ent.Comp.BonusDamage);
            return;
        }

        // Beast's Charge: the eidolon rushes up, stopping a tile short, and hits harder for the run-up.
        var bonus = 0;
        if (ent.Comp.ClosesFeet > 0 && distance > BaseReachTiles)
        {
            var from = _transform.GetWorldPosition(eidolon);
            var to = _transform.GetWorldPosition(args.Target);
            var direction = Vector2.Normalize(to - from);
            _throwing.TryThrow(eidolon, Transform(eidolon).Coordinates.Offset(direction * (distance - 1f)), RushSpeed, doSpin: false);
            if (distance >= 4f)
                bonus = ent.Comp.BonusIfRushed;
        }

        _strikes.PerformStrike(eidolon, args.Target, _strikes.UnarmedStats(eidolon), eidolon, melee: true, attackPenalty: bonus, extraDice: ent.Comp.ExtraDice, bonusDamage: ent.Comp.BonusDamage);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFFastHealingComponent>();
        while (query.MoveNext(out var uid, out var healing))
        {
            if (now >= healing.Until)
            {
                RemCompDeferred<CFFastHealingComponent>(uid);
                continue;
            }

            if (now < healing.Next)
                continue;

            healing.Next = now + Pulse;
            _effects.Heal(uid, uid, healing.PerPulse);
        }
    }
}
