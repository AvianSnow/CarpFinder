using Content.Shared.CF.Crafting;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Crafting;

public sealed class CFWorkbenchBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CFWorkbenchWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CFWorkbenchWindow>();
        _window.OnCraft += recipe => SendMessage(new CFWorkbenchCraftMessage(recipe));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is CFWorkbenchState workbench)
            _window?.UpdateState(workbench);
    }
}
