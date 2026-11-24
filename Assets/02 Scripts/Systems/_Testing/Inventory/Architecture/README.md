# Inventory Architecture – Milestone 3.1: Rotation and Placement Preview

This update builds on the tested Milestone 3 grid foundation. It retains the collection inventory and the existing transfer service, while adding live orientation control and visual placement feedback for grid items.

## What's new

- Press **R** while dragging an item to rotate clockwise through 0°, 90°, 180°, and 270°, then back to 0°. Hold **Shift** and press **R** to rotate counter-clockwise.
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

The shared canvas root receives keyboard focus when a drag begins. While dragging, tap **R** to rotate clockwise or **Shift+R** to rotate counter-clockwise through four orientations. The grid placement stores the exact quarter-turn count, so 180° and 270° persist after the drop rather than collapsing into a simple rotated/unrotated flag. Panels are siblings under the same root, so bringing a panel forward changes visual sibling order without changing its position or a separate document's sorting order. The orientation is stored in the grid placement, not in the shared `ItemDetails` asset, so rotating one instance does not rotate every instance of that item type.

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
7. Press **Shift+R** once from 0°. Confirm the item rotates to 270° (one quarter-turn counter-clockwise). Press it again and confirm 180°, then 90°, then 0°.
7. Move the pointer to a partially out-of-bounds position. Confirm the preview turns red and the drop is rejected.
8. Move the pointer over another item. Confirm the preview turns red and the drop is rejected.
9. Move a grid item within its own inventory. Confirm its own current cells do not make the preview red.
10. Rotate a grid item and move it locally. Confirm its new footprint is retained after dropping.
11. Transfer an item from a collection inventory to a grid while rotated. Confirm the destination uses the rotated footprint.
12. Transfer an item between two grids while rotated. Confirm the destination placement has the rotated dimensions.
13. Force an invalid destination and confirm the item remains in its original inventory and original orientation.
14. Recheck collection-to-collection transfers and grid-to-collection transfers.

The package has been source-reviewed and ZIP-validated, but it has not been compiled or run inside Unity by the assistant. Unity Editor validation is required.

## Where to add item interactions

There are two main entry points, depending on the kind of input:

- **Mouse / item press actions:** `Architecture/Presentation/InventoryPanelView.cs`, in `RegisterItemPointerHandler`. This callback is registered on each item's visible hit target. It currently handles left-click drag and Shift+left-click quick move. Add new pointer actions here (for example right-click inspect, double-click use, or another modifier-click). Keep this method focused on reading the input and forwarding a request; put inventory rules or mutations in `InventoryPanelSession` or an appropriate service/model.
- **Keyboard shortcuts while dragging:** `Architecture/Presentation/InventoryPanelCanvas.cs`, in `OnKeyDown`. The shared canvas owns the keyboard callback so shortcuts work consistently across all inventory panels. It currently maps **R** to clockwise rotation and **Shift+R** to counter-clockwise rotation.
- **Shared interaction behavior:** `Architecture/Interaction/InventoryPanelSession.cs`. This is where drag state, the dragged item's orientation, target previews, and cross-panel interaction coordination live. `RotateDraggedItem(int quarterTurnDirection)` accepts `+1` for clockwise and `-1` for counter-clockwise.

A useful pattern for adding a future action is: detect the input in the view/canvas, call a clearly named session method, and let the session delegate domain work to a service/model. That keeps UI event wiring separate from inventory rules and makes the behavior easier to test.

## Equipment Effects – Milestone 1

The equipment model now raises `EquipmentChanged` after successful slot mutations. `EquipmentEffectService` listens to that event and synchronizes modifiers into the engine-independent `AttributeModel`. Equipment item definitions can author flat and additive-percentage effects in the **Equipment Effects** list on `ItemDetails`.

See `EQUIPMENT_EFFECTS_MILESTONE_1.md` at the project root for setup, the runtime connection example, and the isolated test checklist. The service is deliberately not attached to a player component; a future composition layer will decide which equipment model feeds which attribute model.


## Inventory Quality-of-Life – Stacking and Quick Move

### Stack rules

- `ItemDetails.maxStack` controls the maximum quantity in one runtime `Item`; values are clamped to at least 1 in the Inspector.
- Items stack only when they reference the same item definition (`detailsId`). Matching names are not enough.
- `ItemDetails.maxStack` is authoritative for every category, including equipment. Equipment still defaults to `1`, so ordinary weapons and armor remain non-stackable unless explicitly configured otherwise.
- A stackable equipment item (for example, throwing knives) occupies one equipment slot regardless of its quantity. Consuming part of the stack leaves the item equipped until its quantity reaches zero.
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


## Irregular Item Shapes – Milestone 1

Grid items can now use a designer-authored cell mask instead of occupying their full rectangular footprint. The rectangular width and height remain the shape's bounds; only painted cells reserve space. This supports L, T, hook, and other polyomino-like silhouettes while allowing another item to occupy an empty cell inside the same bounding rectangle.

### Designer workflow

1. Select an `ItemDetails` asset.
2. Set **Grid Footprint** Width and Height (1–16 cells).
3. Enable **Use Custom Shape**.
4. Click cells in the inspector's shape painter to toggle occupied cells. Green cells are occupied; dark cells are empty.
5. Use **Fill All** or **Clear All** as a starting point. A custom shape must contain at least one occupied cell.

When **Use Custom Shape** is disabled, the item retains the previous rectangular behavior. The shape mask is stored on the item definition, while each runtime placement stores its own rotation. Pressing R rotates the actual occupied-cell pattern clockwise through 0°, 90°, 180°, and 270°.

### Runtime behavior

- Placement and movement test only occupied cells, not every cell in the bounding rectangle.
- Empty cells inside an item's bounds can be occupied by other items.
- Cell lookup, auto-placement, cross-inventory placement validation, and local moves use the same mask.
- Placement previews highlight only occupied cells.
- The visible item backing is drawn per occupied cell; the item sprite remains a transparent overlay and should ideally have transparent pixels where the shape has gaps.

### Test

Add `IrregularItemShapeTestRunner` to a scene object and run **Run Irregular Item Shape Tests** from its component context menu. It checks L-shape occupancy, placing another item in the notch, and clockwise-rotated masks. Also rerun the existing stacking/quick-move and equipment-effect test runners to guard against regressions.


## Irregular Inventory Layouts – Milestone 2

Grid inventories can now use a designer-authored layout mask. The grid retains a rectangular width and height, but individual cells can be marked as usable or blocked. The existing rectangular behavior remains the default whenever no layout asset is assigned.

### Designer workflow

1. Create an asset with **Assets → Create → Inventory → Grid Layout**.
2. Select the asset and set its Width and Height.
3. Click cells in the layout painter. Green cells are usable; dark cells are blocked. **Enable All** and **Block All** provide quick starting points.
4. Select the GameObject with `InventoryPanelHost`. Enable Grid Storage and assign the new asset to **Grid Layout**.
5. When a layout asset is assigned, its dimensions and painted mask are used; the host's fallback Grid Width and Grid Height are ignored. Leave Grid Layout unassigned to keep a regular rectangular inventory.

A grid model can also be created directly from a layout asset in gameplay code, without first configuring or opening a panel:

```csharp
GridInventoryModel chestInventory = new GridInventoryModel(
    SerializableGuid.NewGuid(), "Chest", chestGridLayout);

// The model can be opened by any panel host later.
panelHost.Open(chestInventory, chestAppearance);
```

The model copies the asset’s dimensions and usable-cell mask into its own runtime state; it does not retain a Unity asset dependency. Editing the asset later does not silently reshape an already-created inventory. Existing explicit-width/height constructors remain available for generated or procedural masks.

### Runtime behavior

- Blocked cells are visibly darker in the inventory panel.
- An item's occupied cells must all land on usable cells. This works together with irregular item footprints, so empty cells in an item's own shape do not need to be usable or occupied.
- Manual placement, rotation previews, local movement, cross-inventory transfers, starting-item auto-placement, and quick move all respect the same mask.
- Inventory capacity reports the number of usable cells rather than the full rectangular area.
- The runtime model receives its own copy of the dimensions and mask. Runtime inventory operations never modify the authoring asset, and editing the asset later does not silently reshape an already-created inventory.

### Test

Add `InventoryGridLayoutTestRunner` to a scene object and run **Run Inventory Grid Layout Tests** from its component context menu. It checks usable-cell capacity, blocked-cell rejection, compatibility with irregular item notches, auto-placement, an entirely blocked layout, and creating a dynamic grid model directly from a layout asset. Also rerun the irregular item shape and stacking/quick-move runners.


## Dynamic inventory panels, appearances, and external item grants

`InventoryPanelHost` is the scene-facing API for a panel. Its configured model is created automatically at startup, but gameplay code can bind a different existing model at runtime:

```csharp
// Reuse the same panel for a chest, then a barrel.
panelHost.Open(chestModel, chestAppearance);
panelHost.Open(barrelModel, barrelAppearance);

// Close the panel without destroying either model.
panelHost.Close();

// Add a runtime item without interacting with UI elements.
if (!panelHost.TryAddItem(rewardItem, out InventoryOperationFailure failure))
    Debug.Log($"Could not add reward: {failure}");

// Or create an item instance from an ItemDetails asset.
panelHost.TryAddItem(coinDetails, 25, out Item createdCoins, out failure);

// Change the current panel's appearance independently of its model.
panelHost.SetAppearance(barrelAppearance);
```

`Open` binds the panel to the supplied model; it does not create, own, or destroy that model. This lets world containers keep their own model instances while a single panel is reused to view them. `Close` unbinds and hides the panel but retains the last model reference and its contents. `TryAddItem` delegates to that retained model even while the panel is closed, so gameplay systems can award loot or collect items without opening the UI. Reopening the panel renders the latest model contents. Grid inventories still use their automatic first-fit placement and equipment inventories still enforce slot compatibility. `IsOpen` indicates whether the host is currently presenting its retained model.

Create appearance assets with **Assets > Create > Inventory > Presentation > Inventory Appearance**. An appearance can provide a panel-scoped USS StyleSheet and optional sprites for the panel, header, normal slots, blocked grid cells, and occupied item cells. The item icon itself remains defined by `ItemDetails.Icon`. Assign a default appearance on `InventoryPanelHost`, or pass/change one through `Open` and `SetAppearance`.

When adding external gameplay systems, call `InventoryPanelHost.TryAddItem` or operate on the inventory model/service directly. Do not search for or modify UI Toolkit slot elements from gameplay code. The model's `Changed` event updates the bound view automatically.



## Right-click stack interactions and drag cancellation

- **Right-click a stack when not already dragging:** pick up the larger half of the stack (rounded up). The remainder stays in the original slot. The held portion is a temporary drag payload, so splitting does not require an empty inventory slot.
- **Right-click an empty grid cell or collection slot while holding a stack:** place one item there. The remaining held quantity continues following the pointer.
- **Right-click a compatible partial stack while holding a stack:** transfer one item into that stack, respecting its maximum quantity.
- **Left-click release:** place or transfer the remaining held portion using the normal validation and rollback path. If the drop is outside an inventory or invalid, any still-held split remainder is restored to the original stack when possible.
- **Q:** drop the currently held portion through the existing `ItemDropped` world-drop event. For a split drag, only the held portion is dropped; the quantity left in the original inventory is unaffected.
- **Close or hide the source inventory during a drag:** cancels the drag, restores any still-held split quantity to the original stack, clears previews, and hides the shared drag ghost. It does not trigger a world drop.

The right-click input is detected in `InventoryPanelView.RegisterItemPointerHandler`; stack mutations and temporary split-payload handling live in `InventoryPanelSession`. Stack compatibility resolves the runtime `ItemDetails` asset ID first and falls back to the cached item definition ID, so older serialized stacks with stale cached IDs can still merge. The original stack remains at full opacity during a split; only a full-stack drag fades its source visual. A compatible partial stack under the pointer receives the green placement preview. Suggested manual checks: right-click a stack of 9 and confirm 5 are held while 4 remain; right-click an empty slot to place one; right-click a compatible partial stack to add one; cancel or close the source panel and confirm the unplaced held quantity is restored; press Q while split and confirm only the held portion is emitted through `ItemDropped`.

## Global Inventory UI Scale

`InventoryPanelCanvas` is the single owner of the global inventory presentation scale. Its **Global Scale** Inspector field defaults to `1.0` and is clamped to `0.5`–`2.0`. Every `InventoryPanelView` registered with that canvas receives the same scale, and the shared drag ghost scales with the panels. Scaling is presentation-only: it does not change inventory dimensions, item footprints, model contents, or panel percentage positions.

Runtime settings menus can call `InventoryPanelCanvas.SetGlobalScale(float)`; `GlobalScale` reads the current value. `GlobalScaleChanged` is raised when the value changes, allowing other HUD presentation components to subscribe and use the same multiplier. The canvas value is the runtime source of truth; persist a player's preference through the game's settings/save system when that is added. Panel dragging uses the scaled visual footprint when clamping against the shared canvas viewport, so changing UI scale changes panel size without changing the viewport bounds.


## Grid Occupancy Information

`IGridInventoryModel` and `GridInventoryModel` expose read-only capacity queries that can be used by gameplay systems without opening or referencing a panel:

- `TotalUsableCells`: total cells allowed by the grid's layout mask. This matches the existing `Capacity` value.
- `OccupiedCells`: number of currently occupied usable cells, counting the actual item shape after its stored rotation. Empty gaps inside irregular item shapes do not count as occupied.
- `AvailableCells`: usable cells minus occupied cells.
- `FillPercentage`: occupied cells divided by usable cells, expressed as a percentage from `0` to `100`. A grid with no usable cells reports `0`.

These values are calculated from the model's current placements instead of maintained as mutable counters. They therefore reflect successful adds, removals, and moves immediately, and stack quantity does not affect spatial occupancy. Query them directly, for example:

```csharp
if (inventory is IGridInventoryModel grid)
{
    Debug.Log($"{grid.OccupiedCells}/{grid.TotalUsableCells} cells used ({grid.FillPercentage:0.#}%)");
}
```

The `InventoryGridLayoutTestRunner` now checks empty capacity, irregular item-shape occupancy, percentage calculations, quantity independence, removal updates, and zero-capacity layouts.


## Equipment Occupancy and Grid-Equivalent Footprints

`IEquipmentInventoryModel` exposes read-only occupancy information independently of the equipment UI:

- `TotalEquipmentSlots`: number of configured named equipment slots.
- `OccupiedEquipmentSlots`: number of those slots that currently contain an item.
- `AvailableEquipmentSlots`: configured slots that are currently empty.
- `TotalGridEquivalentOccupiedCells`: sum of the actual occupied shape cells for every equipped item, as if each item were stored in a grid.
- `TryGetGridFootprint(itemId, out width, out height, out occupiedCells)`: retrieves an individual equipped item's grid footprint from its `ItemDetails`. Width and height are its unrotated grid bounds; occupied cells respect custom irregular item masks.

The grid-equivalent values describe item footprint, not equipment-slot count or item quantity. Stackable equipment occupies one named equipment slot regardless of stack quantity. These are calculated queries, not mutable counters, so they update automatically after equip, unequip, or slot moves. Example:

```csharp
if (equipment is IEquipmentInventoryModel model)
{
    Debug.Log($"{model.OccupiedEquipmentSlots}/{model.TotalEquipmentSlots} equipment slots used");
    Debug.Log($"Equipped items would occupy {model.TotalGridEquivalentOccupiedCells} grid cells");

    if (model.TryGetGridFootprint(itemId, out int width, out int height, out int cells))
        Debug.Log($"Item footprint: {width}x{height}, {cells} occupied cells");
}
```

`EquipmentInventoryTestRunner` now verifies empty and occupied slot counts, aggregate grid-equivalent cells, individual item footprints, and updates after unequipping.

## Quantity Removal – Model API

All inventory models support quantity removal directly, including while their panels are closed:

- `TryRemoveQuantity(itemInstanceId, quantity, out removed, out failure)` removes up to the requested quantity from one stack.
- `TryRemoveQuantity(itemDetails, quantity, out removed, out failure)` removes across matching stacks, in the model's item-list order.
- `TryRemoveQuantityAtIndex(itemIndex, quantity, out removed, out failure)` targets an item-list index. Grid models additionally expose `TryRemoveQuantityAtCell(x, y, quantity, ...)`; fixed-slot models expose `TryRemoveQuantityAt(slotIndex, quantity, ...)`; equipment models expose `TryRemoveQuantityAt(slotId, quantity, ...)`.
- `TryRemoveExact(itemDetails, quantity, out failure)` is all-or-nothing, useful for crafting costs.
- `TryRemoveAll(itemDetails, out removed, out failure)` removes every matching item.

Quantity removal returns the actual amount removed. If fewer than requested are available, the non-exact operations remove what exists. Full stack removal removes the item entry; partial removal raises a stack-changed event. These operations are model-level and do not require a panel to be open.

`InventoryQuantityRemovalTestRunner` includes checks for the APIs and stackable equipment.
