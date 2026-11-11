using Content.Shared.CF.Factions;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Factions;

/// <summary>
/// Draws crossed swords on the left of a player character with PvP on, so others can see who will fight.
/// </summary>
public sealed partial class CFPvpIconSystem : EntitySystem
{
    private static readonly ProtoId<StatusIconPrototype> Icon = "CFPvpIcon";

    [SubscribeLocalEvent]
    private void OnGetStatusIcons(Entity<CFPvpComponent> ent, ref GetStatusIconsEvent args)
    {
        if (ent.Comp.Enabled)
            args.StatusIcons.Add(ProtoMan.Index(Icon));
    }
}
