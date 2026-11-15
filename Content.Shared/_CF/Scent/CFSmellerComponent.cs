// Ported from Starlight (https://github.com/ss14Starlight/space-station-14), their Scent system, with permission.

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Scent;

/// <summary>
/// The Scent sense: a Sniff action that shows the scent trails other creatures left. Given by <c>CFGrantSense</c> with
/// <c>Scent</c>, or in YAML on a monster. The trail is drawn with <see cref="CFScentMarkerComponent"/> entities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CFSmellerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Sniffing;

    [DataField]
    public EntProtoId ToggleAction = "CFActSniff";

    [DataField]
    public EntityUid? ToggleActionEntity;
}

/// <summary>The visibility layer scent marks are drawn on. Stock uses bits 1 to 8.</summary>
public static class CFScentVisibility
{
    public const int Layer = 64;
}
