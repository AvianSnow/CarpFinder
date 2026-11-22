using Robust.Shared.GameStates;

namespace Content.Shared.CF.Terrain;

/// <summary>What makes ground hard to cross. Options that ignore difficult terrain name the causes they ignore.</summary>
public enum CFTerrainCause : byte
{
    /// <summary>Bushes, vines, undergrowth, fungi.</summary>
    Plants,

    /// <summary>Rubble, gravel, uneven stone and earth.</summary>
    Stone,
    Snow,
    Water,
    Sand,

    /// <summary>Tight spaces that are not tight enough to need a Squeeze.</summary>
    Tight,

    /// <summary>A creature making the ground around it hard to cross, such as Hampering Stance.</summary>
    Creature,
}

/// <summary>
/// The tile this stands on is difficult terrain: moving across it takes half Speed, or a third for greater difficult
/// terrain. Put it on a bush, a snowdrift or an area fill. A tile can be difficult itself through
/// <c>cfDifficultTerrain</c> on its <c>tile</c> prototype. <see cref="CFTerrainSystem"/> reads both.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFDifficultTerrainComponent : Component
{
    [DataField(required: true)]
    public CFTerrainCause Cause;

    [DataField]
    public bool Greater;

    /// <summary>Made by magic. Favored Terrain only ignores terrain that is not.</summary>
    [DataField]
    public bool Magical;
}

/// <summary>
/// How a creature moves through difficult terrain, and what it is in right now. Every creature that moves gets one the
/// first time it meets difficult terrain. Grants add to what it ignores.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CFTerrainWalkerComponent : Component
{
    /// <summary>Difficult terrain of these causes is ignored, and greater difficult terrain of them is only difficult.</summary>
    [DataField]
    public HashSet<CFTerrainCause> Ignores = new();

    /// <summary>As <see cref="Ignores"/>, but only for terrain that is not magical (Favored Terrain).</summary>
    [DataField]
    public HashSet<CFTerrainCause> IgnoresNonMagical = new();

    /// <summary>Every difficult terrain is ignored until this time (Light Paws).</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan IgnoreAllUntil;

    /// <summary>The creature can Step while in difficult terrain (Feather Step).</summary>
    [DataField]
    public bool FeatherStep;

    /// <summary>Creatures whose Hampering Stance this creature is inside.</summary>
    public HashSet<EntityUid> HamperedBy = new();

    /// <summary>What its Speed is multiplied by right now: 1, 1/2 or 1/3.</summary>
    [DataField, AutoNetworkedField]
    public float Multiplier = 1f;

    /// <summary>It stands in difficult terrain it does not ignore, whether or not the first square is free.</summary>
    [DataField, AutoNetworkedField]
    public bool InTerrain;

    /// <summary>When it last moved into difficult terrain, for a first square that is free (Dragon Stance).</summary>
    [DataField]
    public TimeSpan EnteredAt;
}
