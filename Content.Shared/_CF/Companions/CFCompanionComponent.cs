using Robust.Shared.GameStates;

namespace Content.Shared.CF.Companions;

/// <summary>
/// A summoned ally: eidolon, animal companion, familiar, or other summon.
/// It keeps its summoner's company and attacks only the creature that summoner points at.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCompanionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Summoner;

    /// <summary>The creature this ally is ordered to attack. Invalid when it is following, fetching, or standing still.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid Target;

    /// <summary>An item the ally is bringing back. Invalid when it is not fetching.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid Fetch;

    /// <summary>True after the summoner points at themself. The ally paths back and stays nearby.</summary>
    [DataField, AutoNetworkedField]
    public bool Following;

    /// <summary>The <c>cfPet</c> this ally was made from, which it levels by. Empty for a summon with no table.</summary>
    [DataField]
    public string PetId = string.Empty;

    /// <summary>The barding the ally was given, which counts toward its AC.</summary>
    [DataField]
    public string Barding = string.Empty;

    /// <summary>Server-only. Frames spent in reach of a fetch item that would not be picked up.</summary>
    public int FetchWaits;
}
