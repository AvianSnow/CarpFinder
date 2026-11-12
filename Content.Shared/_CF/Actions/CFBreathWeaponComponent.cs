using Content.Shared.Actions;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Actions;

/// <summary>
/// A breath weapon or any other area a creature lets loose and then has to wait to use again, such as a dragon's Poison
/// Breath. Goes on an action parented on <c>CFActBreathBase</c>, with <c>CFAreaEffect</c>, <c>CFCheck</c> and
/// <c>CFDegreeEffects</c> saying what it does. The recharge is rolled each use and becomes the action's cooldown, so the
/// HUD shows it. <see cref="CFBreathWeaponSystem"/> runs it, for a player and for the AI alike.
/// </summary>
[RegisterComponent]
public sealed partial class CFBreathWeaponComponent : Component
{
    /// <summary>The recharge is this many dice of <see cref="RechargeDie"/>, in rounds.</summary>
    [DataField]
    public int RechargeDice = 1;

    [DataField]
    public int RechargeDie = 4;

    /// <summary>How long a round is. PF2e's 6 seconds, as everything timed in rounds here.</summary>
    [DataField]
    public float RoundSeconds = 6f;

    /// <summary>
    /// When set, the breath pours out as this reagent: a puddle on each open tile of the area, nearest first, over
    /// <see cref="FlowSeconds"/>. Only once it has filled do the creatures standing in it roll, and the puddles then
    /// evaporate at the reagent's own speed. Left empty, the area resolves at once. <see cref="CFBreathFlowSystem"/>.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;

    /// <summary>Units per tile. 30 to 49 draws as one smooth sheet; 50 or more overflows and spreads past the area.</summary>
    [DataField]
    public float UnitsPerTile = 48f;

    /// <summary>How long the breath takes to reach its farthest tile, and so how long before anyone rolls.</summary>
    [DataField]
    public float FlowSeconds = 2f;
}

/// <summary>
/// A breath pouring out over its area, on an entity of its own in nullspace so two breaths can flow at once and the
/// flow outlives the breather. Server only. Removed once it has filled and resolved.
/// </summary>
[RegisterComponent]
public sealed partial class CFBreathFlowComponent : Component
{
    /// <summary>The breath action: its area, check and effects.</summary>
    [DataField]
    public EntityUid Source;

    [DataField]
    public EntityUid User;

    [DataField]
    public EntityUid Grid;

    [DataField]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField]
    public float UnitsPerTile;

    [DataField]
    public TimeSpan Start;

    /// <summary>When everyone in the liquid rolls.</summary>
    [DataField]
    public TimeSpan End;

    /// <summary>The tiles it reaches, nearest first, each with how long after <see cref="Start"/> it gets there.</summary>
    [DataField]
    public List<CFBreathFlowTile> Tiles = new();

    /// <summary>How many of <see cref="Tiles"/> have been filled.</summary>
    [DataField]
    public int Filled;
}

[DataDefinition]
public partial struct CFBreathFlowTile
{
    [DataField]
    public Vector2i Tile;

    /// <summary>Seconds after the breath leaves.</summary>
    [DataField]
    public float Delay;

    public CFBreathFlowTile(Vector2i tile, float delay)
    {
        Tile = tile;
        Delay = delay;
    }
}

public sealed partial class CFBreathWeaponEvent : WorldTargetActionEvent
{
}

/// <summary>Draconic Momentum: a critical success on a Strike recharges the creature's breath weapons.</summary>
[RegisterComponent]
public sealed partial class CFDraconicMomentumComponent : Component
{
}
