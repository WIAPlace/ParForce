namespace Systems.Inventory.Architecture
{
    /// <summary>Broad classification used by inventory rules.</summary>
    public enum ItemCategory
    {
        General,
        Consumable,
        Quest,
        Equipment
    }

    /// <summary>Specific equipment classification. A slot may accept one or more values.</summary>
    public enum EquipmentType
    {
        None,
        Head,
        Chest,
        Legs,
        Feet,
        Hands,
        MainHand,
        OffHand,
        Ring,
        Necklace,
        Accessory
    }
}
