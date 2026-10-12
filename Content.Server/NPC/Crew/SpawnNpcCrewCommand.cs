using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.Crew;

[AdminCommand(AdminFlags.Fun)]
public sealed class SpawnNpcCrewCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Command => "spawnnpccrew";
    public string Description => "Spawns AI controlled crew members with a job next to you.";
    public string Help => "Usage: spawnnpccrew <jobId> [count]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!_prototypes.HasIndex<JobPrototype>(args[0]))
        {
            shell.WriteError($"Unknown job '{args[0]}'.");
            return;
        }

        var count = 1;
        if (args.Length == 2 && (!int.TryParse(args[1], out count) || count < 1))
        {
            shell.WriteError("Count must be a positive number.");
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError("You need to be attached to an entity to spawn crew next to it.");
            return;
        }

        var coords = _entities.GetComponent<TransformComponent>(player).Coordinates;
        var system = _entities.System<NpcCrewSystem>();

        for (var i = 0; i < count; i++)
        {
            system.TrySpawnCrew(args[0], coords);
        }

        shell.WriteLine($"Spawned {count} {args[0]} NPC crew.");
    }
}
