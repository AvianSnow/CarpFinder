using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CF.Outdoors;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Outdoors;

/// <summary>Turns the map you are on into an outdoor world, from a preset such as <c>CFOutdoorsTemperate</c>.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class CFOutdoorsCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override string Command => "cf_outdoors";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var preset = args.Length > 0 ? args[0] : "CFOutdoorsTemperate";
        if (!_prototypes.TryIndex<EntityPrototype>(preset, out var prototype)
            || !prototype.Components.ContainsKey(EntityManager.ComponentFactory.GetComponentName<CFOutdoorsComponent>()))
        {
            shell.WriteLine(Loc.GetString("cmd-cf_outdoors-unknown", ("preset", preset)));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player
            || EntityManager.GetComponent<TransformComponent>(player).MapUid is not { } map)
        {
            shell.WriteLine(Loc.GetString("cmd-cf_outdoors-no-map"));
            return;
        }

        EntityManager.RemoveComponent<CFOutdoorsComponent>(map);
        EntityManager.AddComponents(map, prototype);
        shell.WriteLine(Loc.GetString("cmd-cf_outdoors-done", ("preset", preset)));
    }
}
