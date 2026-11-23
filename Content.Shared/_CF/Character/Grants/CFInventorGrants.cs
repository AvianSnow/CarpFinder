using Content.Shared.CF.Actions;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Traits;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character.Grants;

/// <summary>
/// An Inventor's weapon innovation: the base weapon the player picked (a <c>Named</c> choice whose options are weapon
/// prototype ids) with this modification on it. With no pick it is a longsword. The weapon goes in the backpack, as any
/// granted item does. See <see cref="CFInventorSystem.MakeWeapon"/>.
/// </summary>
public sealed partial class CFGrantInnovationWeapon : CFGrant
{
    public static readonly EntProtoId DefaultBase = "CFLongsword";

    /// <summary>Traits the modification adds.</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();

    /// <summary>Damage types a modular head switches between.</summary>
    [DataField]
    public List<ProtoId<CFDamageTypePrototype>> Modes = new();

    /// <summary>The damage die is one step larger (Complex Simplicity).</summary>
    [DataField]
    public bool DieStep;

    /// <summary>Two-hand with a die one step larger (Dynamic Weighting).</summary>
    [DataField]
    public bool TwoHand;

    /// <summary>How many more picks after the base are traits to add, as Complex Simplicity lets the player choose two.</summary>
    [DataField]
    public int TraitPicks;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var inventor = entities.EnsureComponent<CFInventorComponent>(args.Mob);

        var baseWeapon = DefaultBase;
        if (Named(features.Pick(args.Mob, args.Source)) is { } picked && prototypes.HasIndex<EntityPrototype>(picked))
            baseWeapon = picked;

        inventor.WeaponBase = baseWeapon;
        inventor.WeaponTraits = new List<ProtoId<CFTraitPrototype>>(Traits);
        inventor.WeaponModes = new List<ProtoId<CFDamageTypePrototype>>(Modes);
        inventor.WeaponDieStep = DieStep;
        inventor.WeaponTwoHand = TwoHand;

        for (var slot = 1; slot <= TraitPicks; slot++)
        {
            if (Named(features.Pick(args.Mob, args.Source, slot)) is { } trait && prototypes.HasIndex<CFTraitPrototype>(trait))
                inventor.WeaponTraits.Add(trait);
        }

        var item = entities.System<CFInventorSystem>().MakeWeapon(args.Mob, inventor, baseWeapon);
        Stow(entities, args.Mob, item);
    }

    /// <summary>The value of a <c>Named</c> pick without its prefix, or null.</summary>
    public static string? Named(string? pick)
    {
        return pick != null && pick.StartsWith(CFOptionChoices.NamedPrefix, StringComparison.Ordinal)
            ? pick[CFOptionChoices.NamedPrefix.Length..]
            : null;
    }

    /// <summary>Puts a granted item in the backpack when it fits. Otherwise it stays where it was spawned, at the character's feet.</summary>
    public static void Stow(IEntityManager entities, EntityUid mob, EntityUid item)
    {
        var inventory = entities.System<InventorySystem>();
        var storage = entities.System<SharedStorageSystem>();
        if (inventory.TryGetSlotEntity(mob, "back", out var bag)
            && entities.TryGetComponent(bag, out StorageComponent? store)
            && storage.CanInsert(bag.Value, item, out _, store))
        {
            storage.Insert(bag.Value, item, out _, mob, store, playSound: false);
        }
    }
}

/// <summary>Dual-Form Weapon: the second base the weapon innovation can switch to, from a <c>Named</c> pick of weapon prototype ids.</summary>
public sealed partial class CFGrantSecondConfiguration : CFGrant
{
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (CFGrantInnovationWeapon.Named(features.Pick(args.Mob, args.Source)) is not { } picked
            || !IoCManager.Resolve<IPrototypeManager>().HasIndex<EntityPrototype>(picked))
        {
            return;
        }

        entities.EnsureComponent<CFInventorComponent>(args.Mob).SecondBase = picked;
    }
}

/// <summary>Variable Core: the innovation explodes with the damage type picked (a <c>DamageType</c> choice) instead of fire.</summary>
public sealed partial class CFGrantVariableCore : CFGrant
{
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, args.Source) is not { } type || !IoCManager.Resolve<IPrototypeManager>().HasIndex<CFDamageTypePrototype>(type))
            return;

        entities.EnsureComponent<CFInventorComponent>(args.Mob).Core = type;
    }
}

/// <summary>
/// A construct innovation's modification. It is kept on the Inventor, and the construct takes it when it spawns, or now if it is
/// already out. See <see cref="CFInventorSystem.ApplyConstructModifications"/>.
/// </summary>
public sealed partial class CFGrantConstructModification : CFGrant
{
    /// <summary>The construct's Speed, in feet (Accelerated Mobility).</summary>
    [DataField]
    public int SpeedFeet;

    /// <summary>Darkvision (Sensory Array).</summary>
    [DataField]
    public bool Darkvision;

    /// <summary>Skills it is trained in (Wonder Gears).</summary>
    [DataField]
    public List<string> Skills = new();

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var inventor = entities.EnsureComponent<CFInventorComponent>(args.Mob);
        if (SpeedFeet > 0)
            inventor.ConstructSpeedFeet = Math.Max(inventor.ConstructSpeedFeet, SpeedFeet);

        inventor.ConstructDarkvision |= Darkvision;
        foreach (var skill in Skills)
        {
            if (!inventor.ConstructSkills.Contains(skill))
                inventor.ConstructSkills.Add(skill);
        }

        var system = entities.System<CFInventorSystem>();
        if (system.Construct(args.Mob) is { } construct)
            system.ApplyConstructModifications(args.Mob, construct);
    }
}

public enum CFInventorOption : byte
{
    /// <summary>Advanced Construct Companion.</summary>
    AdvancedConstruct,

    /// <summary>Built-In Tools: thieves' tools are part of the innovation.</summary>
    BuiltInTools,

    /// <summary>Reverse Engineer: Crafting in place of Thievery.</summary>
    ReverseEngineer,
}

/// <summary>A flag on <see cref="CFInventorComponent"/>.</summary>
public sealed partial class CFGrantInventorOption : CFGrant
{
    [DataField(required: true)]
    public CFInventorOption Option;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var inventor = entities.EnsureComponent<CFInventorComponent>(args.Mob);
        var system = entities.System<CFInventorSystem>();
        switch (Option)
        {
            case CFInventorOption.AdvancedConstruct:
                inventor.AdvancedConstruct = true;
                if (system.Construct(args.Mob) is { } construct)
                    system.ApplyConstructModifications(args.Mob, construct);

                break;
            case CFInventorOption.BuiltInTools:
                inventor.BuiltInTools = true;
                break;
            case CFInventorOption.ReverseEngineer:
                inventor.CraftingForThievery = true;
                break;
        }
    }
}
