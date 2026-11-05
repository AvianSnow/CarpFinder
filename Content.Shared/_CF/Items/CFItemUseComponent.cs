using Robust.Shared.GameStates;

namespace Content.Shared.CF.Items;

/// <summary>What an item does, in terms an NPC can plan with.</summary>
[Flags]
public enum CFItemUse : byte
{
    None = 0,
    Heals = 1 << 0,
    Weapon = 1 << 1,
    Ranged = 1 << 2,
    Light = 1 << 3,
    Key = 1 << 4,
    Thrown = 1 << 5,
    Consumable = 1 << 6,
}

/// <summary>Lets an advanced NPC recognise an item. See the GOAP actions that look for it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFItemUseComponent : Component
{
    [DataField]
    public CFItemUse Uses;
}

/// <summary>What a place is. Mappers tag investigation nodes with these.</summary>
[Flags]
public enum CFNodeTag : byte
{
    None = 0,
    Closet = 1 << 0,
    Corner = 1 << 1,
    Doorway = 1 << 2,
    Corridor = 1 << 3,
    Vent = 1 << 4,
    Stairs = 1 << 5,
    Shadow = 1 << 6,
}
