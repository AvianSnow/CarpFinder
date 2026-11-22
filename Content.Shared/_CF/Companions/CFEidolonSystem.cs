using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

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
}

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

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;

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

        var reach = BaseReachTiles + ent.Comp.ExtraReachFeet / FeetPerTile;
        if ((_transform.GetWorldPosition(eidolon) - _transform.GetWorldPosition(args.Target)).Length() > reach)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-eidolon-out-of-reach"), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (_net.IsServer)
            _strikes.PerformStrike(eidolon, args.Target, _strikes.UnarmedStats(eidolon), eidolon, melee: true, extraDice: ent.Comp.ExtraDice, bonusDamage: ent.Comp.BonusDamage);
    }
}
