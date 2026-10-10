using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Specific;

/// <summary>
/// Gets the healing item picked by <see cref="PickPatientToHealOperator"/> into the NPC's active hand,
/// taking it out of whatever it is stored in if needed.
/// </summary>
public sealed partial class EquipHealingItemOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;

    /// <summary>
    /// Blackboard key holding the healing item.
    /// </summary>
    [DataField]
    public string HealingItemKey = "HealingItem";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(HealingItemKey, out var item, _entManager)
            || _entManager.Deleted(item))
        {
            return HTNOperatorStatus.Failed;
        }

        if (_hands.IsHolding(owner, item, out var hand))
        {
            _hands.SetActiveHand(owner, hand);
            return HTNOperatorStatus.Finished;
        }

        if (!_hands.TryGetEmptyHand(owner, out var emptyHand))
            return HTNOperatorStatus.Failed;

        // Hands can't take things straight out of an equipment slot (e.g. a pocket), take it out of the slot first.
        // Items inside of storage (e.g. a backpack) can be picked up directly.
        if (_inventory.TryGetContainingSlot((item, null, null), out var slot)
            && _inventory.TryGetSlotEntity(owner, slot.Name, out var slotItem)
            && slotItem == item
            && !_inventory.TryUnequip(owner, slot.Name, silent: true))
        {
            return HTNOperatorStatus.Failed;
        }

        if (!_hands.TryPickup(owner, item, emptyHand))
            return HTNOperatorStatus.Failed;

        _hands.SetActiveHand(owner, emptyHand);
        return HTNOperatorStatus.Finished;
    }
}
