using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.ZLevels;

/// <summary>On a stair, ladder or hole being placed: makes the matching end on the level at <see cref="Offset"/>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelAutoPortalComponent : Component
{
    /// <summary>-1 is the level below, 1 the level above.</summary>
    [DataField(required: true)]
    public int Offset;

    [DataField(required: true)]
    public EntProtoId OtherSide;
}

/// <summary>Stepping onto this drops a creature to the level below, with falling damage.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelHoleComponent : Component;

/// <summary>
/// A ladder or rope: clicking it Climbs to its linked end. A DC of 0 is a plain move, as the book allows
/// for a trivial climb. Otherwise it is an Athletics check, and a critical failure falls.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelClimbComponent : Component
{
    [DataField]
    public int Dc;

    [DataField]
    public int Actions = 1;
}

/// <summary>Lets ghosts and admins move between levels from the action bar.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFZLevelMoverComponent : Component
{
    [DataField]
    public EntProtoId UpAction = "CFActionZLevelUp";

    [DataField]
    public EntProtoId DownAction = "CFActionZLevelDown";

    [DataField, AutoNetworkedField]
    public EntityUid? UpActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? DownActionEntity;
}

public sealed partial class CFZLevelActionUp : InstantActionEvent;

public sealed partial class CFZLevelActionDown : InstantActionEvent;

/// <summary>The Player Core falling rules.</summary>
public static class CFFallRules
{
    public const int MaxFeet = 1500;

    /// <summary>Half the distance, for any fall over 5 feet. Falls past 1,500 feet count as 1,500.</summary>
    public static int Damage(int feet)
    {
        return feet <= 5 ? 0 : Math.Min(feet, MaxFeet) / 2;
    }
}
