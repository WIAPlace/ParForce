using UnityEngine;

public class ItemDatabaseBootstrap : MonoBehaviour
{
    [SerializeField] private HeldItemDatabase database;

    private void Awake()
    {
        if (database == null)
        {
            Debug.LogError("Item Database is not assigned!");
            return;
        }

        database.RegisterAsGlobalInstance();
    }
}