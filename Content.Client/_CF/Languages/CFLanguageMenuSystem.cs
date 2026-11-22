using Content.Shared.CF.Languages;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Languages;

/// <summary>
/// Tells the language menu when the local player's languages change, and sends its choice to the server.
/// After the client <c>LanguageSystem</c> in Starlight (https://github.com/ss14Starlight/space-station-14).
/// </summary>
public sealed partial class CFLanguageMenuSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>The local player's body, or what it knows or speaks, changed.</summary>
    public event Action? LanguagesChanged;

    /// <summary>The local player's languages. Null for a body with none, which understands everything.</summary>
    public CFLanguagesComponent? LocalLanguages => CompOrNull<CFLanguagesComponent>(_player.LocalEntity);

    public void RequestSpeak(ProtoId<CFLanguagePrototype> language)
    {
        RaiseNetworkEvent(new CFSpeakLanguageEvent { Language = language.Id });
    }

    [SubscribeLocalEvent]
    private void OnState(Entity<CFLanguagesComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Owner == _player.LocalEntity)
            LanguagesChanged?.Invoke();
    }

    [SubscribeLocalEvent]
    private void OnAttached(LocalPlayerAttachedEvent args)
    {
        LanguagesChanged?.Invoke();
    }

    [SubscribeLocalEvent]
    private void OnDetached(LocalPlayerDetachedEvent args)
    {
        LanguagesChanged?.Invoke();
    }
}
