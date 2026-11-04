using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Database")]
public class HeldItemDatabase : ScriptableObject
{
    private static HeldItemDatabase _instance;
    public static HeldItemDatabase Instance
    {
        get
        {
            if (_instance == null)
            {
                // Automatically find and load the database asset from your project resources
                _instance = Resources.Load<HeldItemDatabase>("MasterHeldItemDatabase");
                if (_instance == null)
                {
                    Debug.LogError("HeldItemDatabase: Could not find MasterHeldItemDatabase asset in a 'Resources' folder!");
                }
                else
                {
                    _instance.InitializeLookupDictionary();
                }
            }
            return _instance;
        }
    }

    [Header("Master Item Registry")]
    [SerializeField] private List<HeldItemData> allItems = new List<HeldItemData>();

    // A dictionary provides O(1) lightning-fast lookup performance compared to looping through lists
    private Dictionary<int, HeldItemData> itemLookupCache;

    public void InitializeLookupDictionary()
    {
        if (itemLookupCache != null) return; // Already initialized

        itemLookupCache = new Dictionary<int, HeldItemData>();
        
        foreach (var item in allItems)
        {
            if (item == null) continue;

            if (itemLookupCache.ContainsKey(item.itemID))
            {
                Debug.LogWarning($"HeldItemDatabase: Duplicate ID detected! ID {item.itemID} belongs to both {itemLookupCache[item.itemID].heldItemName} and {item.heldItemName}.");
                continue;
            }

            itemLookupCache.Add(item.itemID, item);
        }
    }

    /// <summary>
    /// Looks up an item across the local registry using its network safe integer ID.
    /// </summary>
    public HeldItemData GetItemByID(int id)
    {
        if (itemLookupCache == null)
        {
            InitializeLookupDictionary();
        }

        if (itemLookupCache.TryGetValue(id, out HeldItemData foundItem))
        {
            return foundItem;
        }

        if (id != -1) // -1 is typically used for "Empty / No Item"
        {
            Debug.LogWarning($"HeldItemDatabase: Requested Item ID {id} was not found in the database registry.");
        }
        
        return null;
    }
}