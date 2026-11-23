using Content.Shared.CF.Combat;
using Content.Shared.CF.Items;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects.Types;

/// <summary>
/// Marks the target for the user's Strikes: each hit deals <see cref="Damage"/> and a <see cref="DamageDie"/> more, for
/// <see cref="DurationSeconds"/> (Vow of Mortal Defiance). The rules give it on the first hit each round.
/// </summary>
public sealed partial class CFMarkEffect : CFEffect
{
    [DataField]
    public int Damage;

    [DataField]
    public int DamageDie;

    [DataField]
    public float DurationSeconds = 600f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || !entities.TryGetComponent(args.User, out CFCombatOptionsComponent? options))
            return;

        options.MarkedTarget = target;
        options.MarkedDamage = Damage;
        options.MarkedDamageDie = DamageDie;
        options.MarkedUntil = IoCManager.Resolve<IGameTiming>().CurTime + TimeSpan.FromSeconds(DurationSeconds);
        entities.Dirty(args.User, options);
    }
}

/// <summary>
/// Lightning Swap: what is in the hands goes into the backpack, or on the floor, and up to two weapons, or a shield and a
/// weapon, come off the belt, back and neck into them.
/// </summary>
public sealed partial class CFLightningSwapEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var user = args.User;
        var hands = entities.System<SharedHandsSystem>();
        var inventory = entities.System<InventorySystem>();
        var storage = entities.System<SharedStorageSystem>();

        EntityUid? pack = inventory.TryGetSlotEntity(user, "back", out var worn) && entities.HasComponent<StorageComponent>(worn.Value) ? worn : null;
        foreach (var held in new List<EntityUid>(hands.EnumerateHeld(user)))
        {
            if (!hands.TryDrop(user, held, checkActionBlocker: false))
                continue;

            if (pack is { } bag)
                storage.Insert(bag, held, out _, user: user, playSound: false);
        }

        var drawn = 0;
        var weapons = 0;
        foreach (var slot in CFEncumbranceSystem.HolsterSlots)
        {
            if (drawn >= 2 || hands.CountFreeHands(user) == 0 || !inventory.TryGetSlotEntity(user, slot, out var item))
                continue;

            var shield = entities.HasComponent<CFShieldComponent>(item.Value);
            if (!shield && !entities.HasComponent<CFWeaponComponent>(item.Value))
                continue;

            // Two weapons, or a shield and a weapon: never two shields.
            if (shield && drawn > weapons)
                continue;

            if (!inventory.TryUnequip(user, slot, silent: true) || !hands.TryPickupAnyHand(user, item.Value, animate: false))
                continue;

            drawn++;
            if (!shield)
                weapons++;
        }
    }
}
