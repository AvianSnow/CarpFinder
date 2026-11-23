using Content.Shared.CF.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Items;

/// <summary>
/// Verdant Weapon: a seed that grows into a weapon in the hand that holds it, and back. Each change is an Interact, 1 action.
/// The weapon is a new one each time, so runes put on it are lost when it returns to seed.
/// </summary>
public sealed partial class CFVerdantSeedSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFActionPoolSystem _pool = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    [SubscribeLocalEvent]
    private void OnSeedVerbs(Entity<CFVerdantSeedComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !_hands.IsHolding(args.User, ent.Owner))
            return;

        var user = args.User;
        foreach (var weapon in ent.Comp.Weapons)
        {
            if (!_prototypes.TryIndex(weapon, out var proto))
                continue;

            var id = weapon;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("cf-verdant-grow", ("weapon", proto.Name)),
                Act = () => TryGrow(user, ent.Owner, id),
            });
        }
    }

    [SubscribeLocalEvent]
    private void OnWeaponVerbs(Entity<CFVerdantWeaponComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !_hands.IsHolding(args.User, ent.Owner))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("cf-verdant-return"),
            Act = () => TryReturn(user, ent),
        });
    }

    /// <summary>Grows a held seed into a weapon, in the same hand when it fits. False if the user cannot pay or does not hold it.</summary>
    public bool TryGrow(EntityUid user, EntityUid seed, EntProtoId weapon)
    {
        if (!TryComp(seed, out CFVerdantSeedComponent? comp) || !comp.Weapons.Contains(weapon))
            return false;

        if (Swap(user, seed, weapon) is not { } grown)
            return false;

        EnsureComp<CFVerdantWeaponComponent>(grown);
        return true;
    }

    /// <summary>Turns a held verdant weapon back into its seed.</summary>
    public bool TryReturn(EntityUid user, Entity<CFVerdantWeaponComponent> weapon)
    {
        return Swap(user, weapon, weapon.Comp.Seed) != null;
    }

    private EntityUid? Swap(EntityUid user, EntityUid held, EntProtoId into)
    {
        if (!_net.IsServer || !_hands.IsHolding(user, held) || !_pool.TrySpendActions(user, 1))
            return null;

        var made = SpawnAttachedTo(into, Transform(user).Coordinates);
        _hands.TryDrop(user, held, checkActionBlocker: false);
        Del(held);
        _hands.TryPickupAnyHand(user, made, animate: false);
        return made;
    }
}
