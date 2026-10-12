using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Medical.Healing;
using Robust.Shared.Containers;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Specific;

/// <summary>
/// Goes down the utility query's list of candidates and picks the first one that the NPC both
/// carries a suitable healing item for and can walk to.
/// </summary>
public sealed partial class PickPatientToHealOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private HealingSystem _healing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    /// <summary>
    /// Blackboard key holding the candidates, as returned by a <see cref="UtilityOperator"/> with
    /// <see cref="UtilityOperator.ReturnTypeResult.EnumerableDescending"/>.
    /// </summary>
    [DataField]
    public string ListKey = "TargetList";

    /// <summary>
    /// Where to store the chosen patient.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>
    /// Where to store the coordinates of the chosen patient.
    /// </summary>
    [DataField]
    public string TargetMoveKey = "TargetCoordinates";

    /// <summary>
    /// Where to store the healing item to use on the chosen patient.
    /// </summary>
    [DataField]
    public string HealingItemKey = "HealingItem";

    /// <summary>
    /// How many containers deep to look for healing items (hands and worn items are depth 0).
    /// </summary>
    private const int SearchDepth = 2;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<IEnumerable<KeyValuePair<EntityUid, float>>>(ListKey, out var patients, _entManager))
            return (false, null);

        var carried = new List<EntityUid>();
        foreach (var item in _inventory.GetHandOrInventoryEntities(owner))
        {
            CollectItems(item, carried, 0);
        }

        foreach (var (patient, _) in patients)
        {
            var tool = FindHealingItem(carried, patient);
            if (tool == null)
                continue;

            // Stop a little short of interaction range so we don't stall right outside of it.
            var pathRange = SharedInteractionSystem.InteractionRange - 1f;
            var path = await _pathfinding.GetPath(owner, patient, pathRange, cancelToken);

            if (path.Result == PathResult.NoPath)
                continue;

            return (true, new Dictionary<string, object>
            {
                { TargetKey, patient },
                { TargetMoveKey, _entManager.GetComponent<TransformComponent>(patient).Coordinates },
                { HealingItemKey, tool.Value },
                { NPCBlackboard.PathfindKey, path },
            });
        }

        return (false, null);
    }

    private EntityUid? FindHealingItem(List<EntityUid> carried, EntityUid patient)
    {
        foreach (var item in carried)
        {
            if (_healing.CanHeal(item, patient))
                return item;
        }

        return null;
    }

    /// <summary>
    /// Adds the item and everything inside it (up to <see cref="SearchDepth"/>) to the list.
    /// </summary>
    private void CollectItems(EntityUid item, List<EntityUid> items, int depth)
    {
        items.Add(item);

        if (depth >= SearchDepth || !_entManager.HasComponent<ContainerManagerComponent>(item))
            return;

        foreach (var container in _container.GetAllContainers(item))
        {
            foreach (var contained in container.ContainedEntities)
            {
                CollectItems(contained, items, depth + 1);
            }
        }
    }
}
