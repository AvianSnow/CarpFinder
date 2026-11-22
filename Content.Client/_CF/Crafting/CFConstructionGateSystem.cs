using Content.Client.Construction;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Robust.Client.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Client.CF.Crafting;

/// <summary>
/// Hides the stock crafting menu, button and key alike, from characters untrained in Crafting.
/// The server refuses their requests as well; this only keeps the menu out of their way.
/// </summary>
public sealed partial class CFConstructionGateSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private CFCraftingSystem _crafting = default!;

    [SubscribeLocalEvent(after: [typeof(ConstructionSystem)])]
    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        _construction.UpdateCraftingAvailability(_crafting.CanUseConstructionMenu(args.Entity));
    }

    /// <summary>A character's stats arrive after the player attaches, and training can change.</summary>
    [SubscribeLocalEvent]
    private void OnCombatantState(Entity<CFCombatantComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Owner == _player.LocalEntity)
            _construction.UpdateCraftingAvailability(_crafting.CanUseConstructionMenu(ent));
    }
}
