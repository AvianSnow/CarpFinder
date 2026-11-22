using System.Linq;
using Content.Shared.CF;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.Player;

namespace Content.Server.GameTicking;

public sealed partial class ServerGameTicker
{
    /// <summary>
    /// False, and tells the player why, if this character is not finished. With no lobby there is nowhere to finish
    /// one, and random characters are not the player's to finish, so neither is held back.
    /// </summary>
    private bool CFCharacterCanJoin(ICommonSession player, HumanoidCharacterProfile character)
    {
        if (!LobbyEnabled || _randomizeCharacters || !Cfg.GetCVar(CFCVars.RequireCompleteCharacter))
            return true;

        var missing = CFCharacterRequirements.Missing(ProtoMan, character);
        if (missing.Count == 0)
            return true;

        var list = string.Join(", ", missing.Select(id => Loc.GetString(id)));
        _chatManager.DispatchServerMessage(player, Loc.GetString("cf-join-incomplete", ("missing", list)));
        return false;
    }
}
