using System;
using System.Collections.Generic;
using Systems.Inventory;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Link Database")]
public class ItemLinkDatabase : ScriptableObject 
{
    [System.Serializable]
    public struct ItemLink
    {
        public ItemDetails inventoryItem;
        public HeldItemData heldItem;
    }

    // A clean, public static reference populated explicitly at runtime
    public static ItemLinkDatabase Instance { get; private set; }

    [Header("Master Link Registry")]
    [SerializeField] private List<ItemLink> allLinks = new List<ItemLink>();

    private Dictionary<SerializableGuid, ushort> itemLookupCache;

    public void RegisterAsGlobalInstance()
    {
        Instance = this;
        InitializeLookupDictionary();
    }

    private void InitializeLookupDictionary() 
    {
        if (itemLookupCache != null) return; 
        itemLookupCache = new Dictionary<SerializableGuid, ushort>();

        foreach (var item in allLinks) 
        {
            if (item.inventoryItem.Id == SerializableGuid.Empty || item.heldItem.id == 0) continue;
            if (itemLookupCache.ContainsKey(item.inventoryItem.Id)) continue;
            
            itemLookupCache.Add(item.inventoryItem.Id, item.heldItem.id);
        }
    }

    public ushort GetItemByID(SerializableGuid id) 
    {
        if (itemLookupCache == null) InitializeLookupDictionary();
        return itemLookupCache.TryGetValue(id, out var foundItem) ? foundItem : (ushort)0;
    }
}

