# Inventory Architecture – Milestone 2.1: Targeted Slot Movement

This update fixes the first drag-and-drop interaction pass:

- Each collection inventory now has stable, indexed slots (empty slots are preserved).
- Dragging an item onto an empty slot in the same inventory rearranges it locally.
- Dragging an item onto an empty slot in another inventory transfers it into that exact slot.
- Dropping onto an occupied slot or outside a slot is rejected; the item remains where it was.
- Panel hit testing uses the visible inventory panel bounds rather than the full-screen UIDocument bounds.

## Setup

The existing scene setup is unchanged. Each inventory panel still uses:
- `UIDocument`
- `InventoryPanelView`
- `InventoryPanelHost`
- A shared `InventoryPanelSession` referenced by both panel hosts/views
- `InventoryPanels.uss` assigned as the interaction style sheet

Keep the player and chest panel positions distinct in their `InventoryPanelView` components.

## Current scope and limitations

This milestone uses a simple fixed-slot collection model. Every item occupies exactly one slot. It supports local rearrangement and exact-slot cross-inventory transfers, but does not yet support item footprints, rotation, stacking into an occupied stack, swapping two occupied slots, or equipment slot restrictions. Those are separate capabilities planned for the grid/equipment milestones.

The `Items` property now represents slot positions: it has `Capacity` entries, and empty positions are `null`. Code that previously treated `Items` as a compact list of only occupied items must account for empty slots.

## Test checklist

1. Drag an item to a different empty slot in its own inventory.
2. Drag an item from the player inventory onto a particular empty chest slot.
3. Drag an item from the chest onto a particular empty player slot.
4. Drop onto an occupied slot: item should remain in its original slot.
5. Drop outside both inventory panels or outside the slot grid: no movement.
6. Confirm the other inventory does not receive drops merely because it is in the upper/lower half of the screen.

The transfer service remains independent of UI Toolkit. The panel session only identifies the target panel and slot, then calls the model/service operation.
