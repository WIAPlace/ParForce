namespace Systems.Inventory.Architecture
{
    internal static class InventoryItemMatching
    {
        public static bool MatchesDefinition(Item item, ItemDetails definition)
        {
            if (item == null || definition == null) return false;
            if (item.details != null && item.details.Id != SerializableGuid.Empty)
                return item.details.Id == definition.Id;
            return item.detailsId == definition.Id;
        }

        public static int GetMaxStack(Item item) => item != null && item.details != null
            ? System.Math.Max(1, item.details.maxStack) : 1;
    }
}
