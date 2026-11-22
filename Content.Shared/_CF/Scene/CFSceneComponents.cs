using Content.Shared.CF.Checks;
using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Scene;

/// <summary>
/// The DM describing a scene: tells each player <see cref="Text"/> once, when they walk into range.
/// Everything here is plain text or numbers so it can be edited live with the VV menu, and it saves with the map.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFNarateComponent : Component
{
    public static readonly ProtoId<SourcePortPrototype> Port = "CFNarated";

    /// <summary>What the player is told, such as "A rickety bridge spans the passage".</summary>
    [DataField]
    public string Text = string.Empty;

    /// <summary>How close a player must come, in feet. 5 feet is one tile.</summary>
    [DataField]
    public float RadiusFeet = 15f;

    /// <summary>Players already told. Not saved, so a loaded map starts fresh.</summary>
    [ViewVariables]
    public HashSet<NetUserId> Heard = new();

    [ViewVariables]
    public TimeSpan NextCheck;
}

/// <summary>
/// A skill check a player starts by right-clicking. Each degree of success has its own message and signal.
/// Works on the trigger entity or on any object it is added to.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFSkillCheckComponent : Component
{
    public static readonly ProtoId<SourcePortPrototype> CriticalSuccessPort = "CFSkillCriticalSuccess";
    public static readonly ProtoId<SourcePortPrototype> SuccessPort = "CFSkillSuccess";
    public static readonly ProtoId<SourcePortPrototype> FailurePort = "CFSkillFailure";
    public static readonly ProtoId<SourcePortPrototype> CriticalFailurePort = "CFSkillCriticalFailure";

    /// <summary>
    /// Skills the player can choose from, separated by commas: "Athletics, Acrobatics". Perception works too.
    /// This is one line of text because the VV menu cannot add to a list.
    /// </summary>
    [DataField]
    public string Skills = "Perception";

    [DataField]
    public int Dc = 15;

    [DataField]
    public string CriticalSuccess = string.Empty;

    [DataField]
    public string Success = string.Empty;

    [DataField]
    public string Failure = string.Empty;

    [DataField]
    public string CriticalFailure = string.Empty;

    /// <summary>Each player gets one attempt. Turn off for something they may keep trying.</summary>
    [DataField]
    public bool OncePerPlayer = true;

    /// <summary>Players who have rolled. Not saved, so a loaded map starts fresh.</summary>
    [ViewVariables]
    public HashSet<NetUserId> Rolled = new();
}

/// <summary>Raised on a narrate trigger when it tells a player. For components that react to it directly.</summary>
[ByRefEvent]
public readonly record struct CFNaratedEvent(EntityUid Player);

/// <summary>Raised on a skill check after a roll. For components that react to it directly.</summary>
[ByRefEvent]
public readonly record struct CFSkillCheckRolledEvent(EntityUid Player, string Skill, CFDegree Degree, int Total);
