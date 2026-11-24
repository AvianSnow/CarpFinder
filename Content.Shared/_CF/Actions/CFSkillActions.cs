using Content.Shared.DoAfter;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Actions;

/// <summary>
/// What the right-click skill actions keep on a creature: when it may forage or look for tracks again, how long it
/// can see trails, and whether it is covering its own. The logic is server-side, in <c>CFSkillActionSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSkillActionsComponent : Component
{
    /// <summary>Covering its tracks: half Speed, and it leaves no scent trail. Networked, because movement is predicted.</summary>
    [DataField, AutoNetworkedField]
    public bool CoveringTracks;

    [DataField]
    public TimeSpan NextSubsist;

    [DataField]
    public TimeSpan NextTrack;

    /// <summary>Until when a successful Track shows trails. Zero when it does not.</summary>
    [DataField]
    public TimeSpan TrackingUntil;

    /// <summary>Track gave the creature its trail sight, so it goes when the time runs out.</summary>
    [DataField]
    public bool BorrowedSmeller;
}

[Serializable, NetSerializable]
public sealed partial class CFRepairDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CFSubsistDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CFTrackDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CFTreatDiseaseDoAfterEvent : SimpleDoAfterEvent;

/// <summary>The part of the skill actions both sides run: covering tracks halves Speed.</summary>
public sealed partial class CFSharedSkillActionSystem : EntitySystem
{
    public const float CoverTracksSpeed = 0.5f;

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<CFSkillActionsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.CoveringTracks)
            args.ModifySpeed(CoverTracksSpeed);
    }
}
