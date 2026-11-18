using Content.Shared.Whitelist;

namespace Content.Shared.CF.Effects;

/// <summary>
/// The user picks a mode from a radial menu before the source is used.
/// Prestidigitation's tricks, and later variable-action spells and item activations.
/// </summary>
[RegisterComponent]
public sealed partial class CFModeChoiceComponent : Component
{
    [DataField(required: true)]
    public List<CFModeOption> Modes = new();

    /// <summary>Mode value for this use. 0 is no mode. The server copies it from the perform request.</summary>
    [DataField]
    public byte Chosen;

    public CFModeOption? ChosenOption()
    {
        if (Chosen == 0)
            return null;

        foreach (var mode in Modes)
        {
            if (mode.Value == Chosen)
                return mode;
        }

        return null;
    }
}

[DataDefinition]
public sealed partial class CFModeOption
{
    /// <summary>Sent on the request. Must not be 0. Systems that handle the mode read this value.</summary>
    [DataField(required: true)]
    public byte Value;

    /// <summary>Locale id of the menu label.</summary>
    [DataField(required: true)]
    public string Name = "";

    /// <summary>Locale id of the tooltip.</summary>
    [DataField]
    public string? Tooltip;

    /// <summary>Happens on pick, with no click (<see cref="CFModeImmediateEvent"/>).</summary>
    [DataField]
    public bool Immediate;

    /// <summary>
    /// The click picks an object, not a creature. Entities under the cursor must match this whitelist.
    /// </summary>
    [DataField]
    public EntityWhitelist? ObjectWhitelist;

    [DataField]
    public EntityWhitelist? ObjectBlacklist;

    public bool TargetsObject => ObjectWhitelist != null;

    /// <summary>What this mode costs in actions. 0 leaves the action's own cost (Heal's 1, 2 or 3 actions).</summary>
    [DataField]
    public int Actions;

    /// <summary>The range in this mode, in feet. 0 keeps the source's own.</summary>
    [DataField]
    public int RangeFeet;

    /// <summary>This mode has a range of touch.</summary>
    [DataField]
    public bool Touch;

    /// <summary>
    /// The mode affects an area instead of one creature: around the user for an <see cref="Immediate"/> mode, or aimed at
    /// the creature clicked (a cone toward it). Everyone the area holds is affected except the user, as for any emanation.
    /// </summary>
    [DataField]
    public CFAreaShape? AreaShape;

    [DataField]
    public int AreaFeet;

    /// <summary>Who the area affects.</summary>
    [DataField]
    public CFTargetFilter AreaFilter = CFTargetFilter.All;

    /// <summary>The damage type this mode picks, for effects that read it (Resist Energy's energy).</summary>
    [DataField]
    public Robust.Shared.Prototypes.ProtoId<Content.Shared.CF.DamageTypes.CFDamageTypePrototype>? DamageType;

    /// <summary>The mode is only offered to a creature that has this (Directed Channel's cone).</summary>
    [DataField]
    public CFModeRequirement Requires;
}

public enum CFModeRequirement : byte
{
    None,

    /// <summary>The Directed Channel feat: <c>CFCombatOptionsComponent.DirectedChannel</c>.</summary>
    DirectedChannel,
}

public static class CFModeRequirements
{
    public static bool Met(IEntityManager entities, EntityUid user, CFModeOption mode)
    {
        return mode.Requires switch
        {
            CFModeRequirement.DirectedChannel => entities.GetComponentOrNull<Content.Shared.CF.Combat.CFCombatOptionsComponent>(user)?.DirectedChannel == true,
            _ => true,
        };
    }
}
