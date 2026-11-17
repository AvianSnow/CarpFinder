// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Server.Administration;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.DayCycle;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.CF.ZLevels.DayCycle;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class CFSetTimeCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "cf_znetwork_time_set";
    public override string Description => "Sets the current time of day for all maps in zNetwork";
    public override string Help => "cf_znetwork_time_set <net entity> <seconds/hh:mm:ss>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Not enough arguments!");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var targetNet) ||
            !_entities.TryGetEntity(targetNet, out var target))
        {
            shell.WriteError($"Unable to find entity {args[0]}");
            return;
        }

        if (!_entities.TryGetComponent<CFZMapNetworkComponent>(target, out var levelComp))
        {
            shell.WriteError($"Target entity doesnt have CFZLevelsNetworkComponent {args[0]}");
            return;
        }

        if (!TryParseTime(args[1], out var time))
        {
            shell.WriteError("Time is in the wrong format! Use seconds or hh:mm:ss");
            return;
        }

        _entities.System<CFZNetworkTimeSystem>().SetTime((target.Value, levelComp), time);
    }

    private static bool TryParseTime(string arg, out TimeSpan time)
    {
        if (int.TryParse(arg, out var seconds))
        {
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return TimeSpan.TryParse(arg, out time);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = new List<CompletionOption>();
            var query = _entities.EntityQueryEnumerator<CFZMapNetworkComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                options.Add(new CompletionOption(_entities.GetNetEntity(uid).ToString(), meta.EntityName));
            }
            return CompletionResult.FromHintOptions(options, "zNetwork net entity");
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHint("Time of day, in seconds or hh:mm:ss");
        }

        return CompletionResult.Empty;
    }
}
