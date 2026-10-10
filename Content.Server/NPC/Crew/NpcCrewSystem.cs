using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Spawners.Components;
using Content.Shared.Station.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Crew;

/// <summary>
/// Spawns NPC crew: AI controlled station workers with a job, outfit and ID.
/// For now they only wander around; the root task <see cref="RootTask"/> is where
/// job-specific behaviors (repairing, cooking, healing, responding to crime) will go.
/// </summary>
public sealed class NpcCrewSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;
    [Dependency] private StationSystem _station = default!;

    private const string DefaultRootTask = "NpcCrewCompound";

    /// <summary>
    /// Jobs that get their own behavior instead of <see cref="DefaultRootTask"/>.
    /// </summary>
    private static readonly Dictionary<ProtoId<JobPrototype>, string> RootTasks = new()
    {
        ["MedicalDoctor"] = "NpcDoctorCompound",
    };

    /// <summary>
    /// Items a job's NPC crew carry that their behavior relies on, by inventory slot.
    /// </summary>
    private static readonly Dictionary<ProtoId<JobPrototype>, (string Slot, EntProtoId Item)[]> ExtraGear = new()
    {
        ["MedicalDoctor"] =
        [
            ("pocket1", "Brutepack"),
            ("pocket2", "Ointment"),
        ],
    };

    private static readonly ProtoId<NpcFactionPrototype> CrewFaction = "NanoTrasen";

    /// <summary>
    /// How long after the round starts before crew is spawned, so stations and spawn points are ready.
    /// </summary>
    private static readonly TimeSpan SpawnDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Jobs picked from at random for round start crew.
    /// </summary>
    private static readonly ProtoId<JobPrototype>[] RoundstartJobs =
    [
        "Chef",
        "Janitor",
        "Bartender",
        "StationEngineer",
        "AtmosphericTechnician",
        "MedicalDoctor",
        "SecurityOfficer",
        "Botanist",
        "Scientist",
        "CargoTechnician",
        "Passenger",
    ];

    private bool _spawnedThisRound;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _spawnedThisRound = false);
    }

    public override void Update(float frameTime)
    {
        if (_spawnedThisRound
            || _ticker.RunLevel != GameRunLevel.InRound
            || _timing.CurTime < _ticker.RoundStartTimeSpan + SpawnDelay)
        {
            return;
        }

        _spawnedThisRound = true;

        var count = _cfg.GetCVar(CCVars.NPCCrewRoundstartCount);
        for (var i = 0; i < count; i++)
        {
            TrySpawnCrew(_random.Pick(RoundstartJobs));
        }
    }

    /// <summary>
    /// Spawns an NPC crew member with the given job.
    /// </summary>
    /// <param name="job">The job to give them.</param>
    /// <param name="at">Where to spawn them. Defaults to one of the job's spawn points on the first station.</param>
    /// <returns>The spawned entity, or null if there was nowhere to spawn it.</returns>
    public EntityUid? TrySpawnCrew(ProtoId<JobPrototype> job, EntityCoordinates? at = null)
    {
        var station = at is { } coords
            ? _station.GetOwningStation(coords.EntityId)
            : _station.GetStations().FirstOrDefault().Owner;

        if (at == null && !TryGetSpawnPoint(job, station, out at))
            return null;

        var profile = HumanoidCharacterProfile.Random();
        var uid = _spawning.SpawnPlayerMob(at!.Value, job, profile, station);

        // The humanoid prototype has no movement or AI of its own, players get movement via their session.
        EnsureComp<InputMoverComponent>(uid);
        EnsureComp<MobMoverComponent>(uid);
        _faction.AddFaction(uid, CrewFaction);

        EnsureComp<NpcCrewComponent>(uid).Job = job;

        if (ExtraGear.TryGetValue(job, out var gear))
        {
            foreach (var (slot, item) in gear)
            {
                _inventory.SpawnItemInSlot(uid, slot, item, silent: true, force: true);
            }
        }

        var rootTask = RootTasks.GetValueOrDefault(job, DefaultRootTask);
        AddComp<HTNComponent>(uid).RootTask = new HTNCompoundTask { Task = rootTask };

        // The AI normally wakes up on map init, but the entity has already been map-initialized by now.
        _npc.WakeNPC(uid);

        return uid;
    }

    private bool TryGetSpawnPoint(ProtoId<JobPrototype> job, EntityUid? station, out EntityCoordinates? coords)
    {
        var matching = new List<EntityCoordinates>();
        var generic = new List<EntityCoordinates>();
        var lateJoin = new List<EntityCoordinates>();

        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (station != null && _station.GetOwningStation(uid, xform) != station)
                continue;

            switch (point.SpawnType)
            {
                case SpawnPointType.Job when point.Job == job:
                    matching.Add(xform.Coordinates);
                    break;
                case SpawnPointType.Job when point.Job == null:
                    generic.Add(xform.Coordinates);
                    break;
                case SpawnPointType.LateJoin:
                    lateJoin.Add(xform.Coordinates);
                    break;
            }
        }

        var options = matching.Count > 0 ? matching : generic.Count > 0 ? generic : lateJoin;
        if (options.Count == 0)
        {
            coords = null;
            return false;
        }

        coords = _random.Pick(options);
        return true;
    }
}
