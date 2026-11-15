using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.CF.Languages;

/// <summary>
/// Opens the language menu from its button on the top bar. It has no key, because upstream keybinds are not edited.
/// After <c>LanguageMenuUIController</c> in Starlight (https://github.com/ss14Starlight/space-station-14).
/// </summary>
[UsedImplicitly]
public sealed class CFLanguageMenuUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private CFLanguageMenuWindow? _window;

    private MenuButton? LanguageButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.CFLanguageButton;

    public void OnStateEntered(GameplayState state)
    {
        _window = UIManager.CreateWindow<CFLanguageMenuWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);
        _window.OnClose += () => SetPressed(false);
        _window.OnOpen += () => SetPressed(true);
    }

    public void OnStateExited(GameplayState state)
    {
        _window?.Close();
        _window = null;
    }

    public void UnloadButton()
    {
        if (LanguageButton != null)
            LanguageButton.OnPressed -= OnButtonPressed;
    }

    public void LoadButton()
    {
        if (LanguageButton != null)
            LanguageButton.OnPressed += OnButtonPressed;
    }

    private void OnButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void SetPressed(bool pressed)
    {
        if (LanguageButton != null)
            LanguageButton.Pressed = pressed;
    }

    public void ToggleWindow()
    {
        if (_window == null)
            return;

        if (_window.IsOpen)
            _window.Close();
        else
            _window.Open();
    }
}
