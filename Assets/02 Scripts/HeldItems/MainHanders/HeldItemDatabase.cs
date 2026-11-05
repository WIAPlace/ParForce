using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Database")]
public class HeldItemDatabase : ScriptableObject 
{
    // A clean, public static reference populated explicitly at runtime
    public static HeldItemDatabase Instance { get; private set; }

    [Header("Master Item Registry")]
    [SerializeField] private List<HeldItemData> allItems = new List<HeldItemData>();

    private Dictionary<int, HeldItemData> itemLookupCache;

    public void RegisterAsGlobalInstance()
    {
        Instance = this;
        InitializeLookupDictionary();
    }

    private void InitializeLookupDictionary() 
    {
        if (itemLookupCache != null) return; 
        itemLookupCache = new Dictionary<int, HeldItemData>();

        foreach (var item in allItems) 
        {
            if (item == null) continue;
            if (itemLookupCache.ContainsKey(item.itemID)) continue;
            
            itemLookupCache.Add(item.itemID, item);
        }
    }

    public HeldItemData GetItemByID(int id) 
    {
        if (itemLookupCache == null) InitializeLookupDictionary();
        return itemLookupCache.TryGetValue(id, out var foundItem) ? foundItem : null;
    }
}

