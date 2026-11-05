using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server.CF.ZLevels;

/// <summary>Joins existing maps into a stack of levels, listed from the bottom up.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class CFCombineZLevelsCommand : LocalizedEntityCommands
{
    public override string Command => "cf_combinezlevels";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Loc.GetString("cmd-cf_combinezlevels-help"));
            return;
        }

        var levels = EntityManager.System<CFZLevelSystem>();
        var maps = EntityManager.System<SharedMapSystem>();
        var ids = new List<MapId>();
        foreach (var arg in args)
        {
            if (!int.TryParse(arg, out var number) || !maps.MapExists(new MapId(number)))
            {
                shell.WriteLine(Loc.GetString("cmd-cf_combinezlevels-bad-map", ("map", arg)));
                return;
            }

            var id = new MapId(number);
            if (levels.InNetwork(id) || ids.Contains(id))
            {
                shell.WriteLine(Loc.GetString("cmd-cf_combinezlevels-used", ("map", arg)));
                return;
            }

            ids.Add(id);
        }

        levels.Combine(ids);
        shell.WriteLine(Loc.GetString("cmd-cf_combinezlevels-done", ("count", ids.Count)));
    }
}
