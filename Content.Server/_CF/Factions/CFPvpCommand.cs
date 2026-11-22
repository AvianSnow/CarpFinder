using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CF.Factions;
using Robust.Shared.Console;

namespace Content.Server.CF.Factions;

/// <summary>
/// <c>pvp on</c>, <c>pvp off</c>, or <c>pvp</c> to toggle. Typed in chat as <c>/pvp on</c>.
/// </summary>
[AnyCommand]
public sealed class CFPvpCommand : LocalizedEntityCommands
{
    public override string Command => "pvp";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } mob
            || !EntityManager.TryGetComponent(mob, out CFPvpComponent? pvp))
        {
            shell.WriteError(Loc.GetString("cmd-pvp-no-character"));
            return;
        }

        bool enabled;
        switch (args.Length == 0 ? null : args[0].ToLowerInvariant())
        {
            case null:
                enabled = !pvp.Enabled;
                break;
            case "on":
                enabled = true;
                break;
            case "off":
                enabled = false;
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-pvp-help"));
                return;
        }

        var factions = EntityManager.System<CFFactionSystem>();
        if (!factions.TrySetPvp((mob, pvp), enabled))
        {
            shell.WriteError(Loc.GetString("cmd-pvp-locked", ("seconds", factions.PvpLockSeconds((mob, pvp)))));
            return;
        }

        shell.WriteLine(Loc.GetString(enabled ? "cmd-pvp-on" : "cmd-pvp-off"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(new[] { "on", "off" })
            : CompletionResult.Empty;
    }
}
