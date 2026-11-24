# Inventory Architecture – Milestone 3.1: Rotation and Placement Preview

This update builds on the tested Milestone 3 grid foundation. It retains the collection inventory and the existing transfer service, while adding live orientation control and visual placement feedback for grid items.

## What's new

- Press **R** while dragging an item to rotate clockwise through 0°, 90°, 180°, and 270°, then back to 0°.
- The dragged item's current orientation is respected when a grid item is picked up.
- A live footprint preview follows the target cell. Green means the full footprint can be placed; red means it is out of bounds or overlaps another item.
- Local grid moves validate against the moving item itself, so its current cells do not incorrectly block its preview or destination.
- Grid-to-grid and collection-to-grid transfers use the selected orientation.
- Failed cross-inventory transfers restore a grid item's original coordinates and orientation.
- The existing `TryTransferAt` overload remains available and defaults to unrotated placement for compatibility.

## Setup

The presentation now uses one shared canvas. Follow `UI_SHARED_CANVAS_SETUP.md` for the full scene wiring:

1. One shared `UIDocument` + `InventoryPanelCanvas` for the inventory screen.
2. One `InventoryPanelSession` assigned once to the shared canvas.
3. One `InventoryPanelView` and `InventoryPanelHost` per visible inventory; all views reference the same canvas and inherit its session.
4. Assign `InventoryPanels.uss` to the shared canvas, not to each view.
5. Enable Grid Storage on the hosts that should use grid layouts and set their dimensions.
6. Set each `ItemDetails.GridWidth` and `GridHeight` to the item's unrotated footprint.

The shared canvas root receives keyboard focus when a drag begins. While dragging, tap **R** to advance the item clockwise through four orientations. The grid placement stores the exact quarter-turn count, so 180° and 270° persist after the drop rather than collapsing into a simple rotated/unrotated flag. Panels are siblings under the same root, so bringing a panel forward changes visual sibling order without changing its position or a separate document's sorting order. The orientation is stored in the grid placement, not in the shared `ItemDetails` asset, so rotating one instance does not rotate every instance of that item type.

## Behavior notes

- Rotation swaps width and height only at 90° and 270°. At 0° and 180°, the footprint retains its original dimensions. The displayed icon and drag ghost rotate by the exact stored angle.
- The top-left cell under the pointer is the placement anchor.
- The preview follows the grid's responsive cell size. A red preview at an edge still indicates an invalid full footprint.
- When transferring into a collection inventory, rotation has no storage meaning because collection slots are 1x1. A grid item's original orientation is restored if a transfer fails.
- Rotation is per runtime placement. A collection inventory does not retain grid orientation metadata.
- Swapping occupied items and multi-inventory persistence remain future milestones; stacking and equipment slots are covered by the later sections below.

## Test checklist

1. Confirm Unity recompiles without errors.
2. Drag a 2x3 item over an empty area of a grid. Confirm the preview is green and covers 2 columns by 3 rows.
3. Press **R** once. Confirm the preview changes to 3 columns by 2 rows and the icon turns 90° clockwise.
4. Press **R** again. Confirm the icon turns to 180° and the footprint returns to 2 columns by 3 rows.
5. Press **R** again. Confirm the icon turns to 270° and the footprint changes to 3 columns by 2 rows.
6. Press **R** again. Confirm the icon returns to 0° and the original 2-by-3 footprint.
7. Move the pointer to a partially out-of-bounds position. Confirm the preview turns red and the drop is rejected.
8. Move the pointer over another item. Confirm the preview turns red and the drop is rejected.
9. Move a grid item within its own inventory. Confirm its own current cells do not make the preview red.
10. Rotate a grid item and move it locally. Confirm its new footprint is retained after dropping.
11. Transfer an item from a collection inventory to a grid while rotated. Confirm the destination uses the rotated footprint.
12. Transfer an item between two grids while rotated. Confirm the destination placement has the rotated dimensions.
13. Force an invalid destination and confirm the item remains in its original inventory and original orientation.
14. Recheck collection-to-collection transfers and grid-to-collection transfers.

The package has been source-reviewed and ZIP-validated, but it has not been compiled or run inside Unity by the assistant. Unity Editor validation is required.

## Equipment Effects – Milestone 1

The equipment model now raises `EquipmentChanged` after successful slot mutations. `EquipmentEffectService` listens to that event and synchronizes modifiers into the engine-independent `AttributeModel`. Equipment item definitions can author flat and additive-percentage effects in the **Equipment Effects** list on `ItemDetails`.

See `EQUIPMENT_EFFECTS_MILESTONE_1.md` at the project root for setup, the runtime connection example, and the isolated test checklist. The service is deliberately not attached to a player component; a future composition layer will decide which equipment model feeds which attribute model.


## Inventory Quality-of-Life – Stacking and Quick Move

### Stack rules

- `ItemDetails.maxStack` controls the maximum quantity in one runtime `Item`; values are clamped to at least 1 in the Inspector.
- Items stack only when they reference the same item definition (`detailsId`). Matching names are not enough.
- Equipment items never stack and equipment slots accept exactly one item.
- Stack quantity changes raise `InventoryChangeKind.StackChanged`, so existing model-bound panels refresh their quantity labels.
- Dragging a stack onto another compatible stack merges up to the target's maximum. If the source contains more than the target can accept, the remainder stays in the source.

### Shift-click quick move

- Hold **Shift** and left-click an item.
- Each `InventoryPanelView` has a `Quick Move Priority` field. Higher values are checked first; equal values keep registration order.
- The source panel and any other panel showing the same inventory model are excluded.
- Quick move fills matching partial stacks across all eligible destination panels first. It then places remaining quantities into empty slots, in priority order.
- Equipment panels use their own item-category and equipment-type validation. Non-equipment items skip equipment panels; compatible equipment items can move into the first compatible empty named slot.
- If some destinations are full or incompatible, the item remains in its source or only the unmoved remainder remains there.
- Quick move only targets panels registered with the same shared `InventoryPanelSession`.

### Recommended priority configuration

- Equipment: `3`
- Player backpack: `2`
- Storage / chest: `1`

### Verification

Add `InventoryStackingQuickMoveTestRunner` to a scene GameObject and use its component context menu. It checks partial-stack filling across destinations, no cross-definition merges, safe failure when destinations are full, compatible equipment quick move, and partial direct stack merge. Also manually verify shift-click ordering and all four clockwise icon orientations in the UI after Unity recompiles. The runner includes a model-level check for 0°, 90°, 180°, and 270° footprint/orientation persistence.
