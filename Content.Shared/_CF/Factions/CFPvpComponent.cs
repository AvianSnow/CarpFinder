using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.CF.Factions;

/// <summary>
/// On every player character. Two player characters can harm each other only when both have <see cref="Enabled"/>
/// on, whatever their factions say. Set with the <c>pvp</c> command.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CFPvpComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled;

    /// <summary>When this creature last hurt, or was hurt by, another player character with PvP on. Null is never.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan? LastFight;
}
