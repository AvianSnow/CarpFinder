using Content.Shared.CF.Factions;
using Content.Shared.Mobs.Systems;
using Content.Shared.Pointing;
using Content.Shared.Popups;

namespace Content.Server.CF.GOAP.Encounters;

/// <summary>
/// A player in a group's leader orders the rest with the companion rule: point at a living enemy and they go for it.
/// The group stays on its own brains, so it still flanks, and it keeps fighting if the player does nothing.
/// </summary>
public sealed partial class CFGOAPCommandSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFGOAPEncounterSystem _encounters = default!;

    [SubscribeLocalEvent]
    private void OnPointed(Entity<CFGOAPCommanderComponent> ent, ref AfterPointedAtEvent args)
    {
        if (!_mobState.IsAlive(args.Pointed)
            || !_factions.CanHarm(ent, args.Pointed)
            || !_encounters.TryGetGroup(ent, out var group)
            || group.Target == args.Pointed)
            return;

        _encounters.Command(ent, group, args.Pointed);
        _popup.PopupEntity(Loc.GetString("cf-commander-attack"), ent, ent);
    }
}
