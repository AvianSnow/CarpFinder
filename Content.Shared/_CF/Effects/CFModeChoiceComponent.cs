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
}
