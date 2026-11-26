using Robust.Shared.Audio;

namespace Content.Shared.CF.Gathering;

/// <summary>A kind of work a tool does on the world with no attack roll.</summary>
public enum CFToolWork : byte
{
    /// <summary>Felling trees and splitting logs.</summary>
    Chop,

    /// <summary>Breaking rock and ore veins.</summary>
    Mine,
}

/// <summary>
/// A tool that works on a <see cref="CFWorkableComponent"/> of the same kind: each swing counts, with no d20.
/// Against anything else it is whatever weapon it also is.
/// </summary>
[RegisterComponent]
public sealed partial class CFWorkToolComponent : Component
{
    [DataField(required: true)]
    public List<CFToolWork> Kinds = new();
}

/// <summary>
/// Something a tool of the right kind brings down in a fixed number of swings: a tree, a rock, an ore vein.
/// Without the tool it is an object like any other, struck against its Hardness.
/// </summary>
[RegisterComponent]
public sealed partial class CFWorkableComponent : Component
{
    [DataField(required: true)]
    public CFToolWork Kind;

    /// <summary>How many swings it takes.</summary>
    [DataField]
    public int Hits = 3;

    /// <summary>Swings taken so far.</summary>
    [DataField]
    public int Progress;

    /// <summary>Played on each swing. The tool's own hit sound when null.</summary>
    [DataField]
    public SoundSpecifier? Sound;
}
