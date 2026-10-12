using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.Crew;

/// <summary>
/// Marks an entity as an NPC crew member, a station worker controlled by AI rather than a player.
/// </summary>
[RegisterComponent, Access(typeof(NpcCrewSystem))]
public sealed partial class NpcCrewComponent : Component
{
    /// <summary>
    /// The job this crew member was spawned with.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? Job;
}
