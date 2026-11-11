using Robust.Shared.GameStates;

namespace Content.Shared.CF.Companions;

/// <summary>
/// A summoned ally: eidolon, animal companion, familiar, construct, thrall or other summon.
/// It thinks with a GOAP brain: it keeps its summoner's company, attacks enemies by itself, and goes for the creature
/// its summoner points at first.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCompanionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Summoner;

    /// <summary>
    /// The creature its summoner pointed at, which it goes for first while it can see it. Invalid when it chooses for
    /// itself.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid Target;

    /// <summary>An item the ally is bringing back. Invalid when it is not fetching.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid Fetch;

    /// <summary>True after the summoner points at themself, until it is sent somewhere else. It always keeps close.</summary>
    [DataField, AutoNetworkedField]
    public bool Following;

    /// <summary>The <c>cfPet</c> this ally was made from, which it levels by. Empty for a summon with no table.</summary>
    [DataField]
    public string PetId = string.Empty;

    /// <summary>The barding the ally was given, which counts toward its AC.</summary>
    [DataField]
    public string Barding = string.Empty;

    /// <summary>Server-only. Its brain is switched off while it fetches, because the fetch walks it.</summary>
    [DataField]
    public bool BrainPaused;

    /// <summary>Server-only. Frames spent in reach of a fetch item that would not be picked up.</summary>
    public int FetchWaits;
}
