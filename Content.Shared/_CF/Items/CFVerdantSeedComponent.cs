using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Items;

/// <summary>
/// A Druid's verdant seed (Verdant Weapon). Held in hand, a verb grows it into one of <see cref="Weapons"/> for 1 action.
/// The weapon gets <see cref="CFVerdantWeaponComponent"/>, whose verb turns it back.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFVerdantSeedComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> Weapons = new();
}

/// <summary>A weapon grown from a verdant seed. Held in hand, a verb returns it to <see cref="Seed"/> for 1 action.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFVerdantWeaponComponent : Component
{
    [DataField]
    public EntProtoId Seed = "CFVerdantSeed";
}
