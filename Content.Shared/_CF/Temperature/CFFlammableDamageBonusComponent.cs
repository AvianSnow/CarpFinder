using Content.Shared.Damage;

namespace Content.Shared.CF.Temperature;

/// <summary>
/// Add bonus damage to melee attacks per flammable stack
/// </summary>
[RegisterComponent, Access(typeof(CFSharedFireSpreadSystem))]
public sealed partial class CFFlammableBonusDamageComponent : Component
{
    [DataField]
    public DamageSpecifier DamagePerStack = new()
    {
        DamageDict = new()
        {
            {"Heat", 0.3},
        }
    };
}
