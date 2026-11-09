using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Languages;

/// <summary>
/// What a creature can speak and understand. A creature without this speaks plainly and understands everything, so stock
/// mobs, machines and ghosts work as they always did.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFLanguagesComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ProtoId<CFLanguagePrototype>> Known = new();

    /// <summary>What it speaks now. Empty means <see cref="CFLanguageRules.DefaultLanguage"/>, or its first known language.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<CFLanguagePrototype>? Speaking;
}
