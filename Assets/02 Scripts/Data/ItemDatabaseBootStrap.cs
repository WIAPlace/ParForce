using System;
using UnityEngine;

public class ItemDatabaseBootstrap : MonoBehaviour
{
    [SerializeField] private HeldItemDatabase heldDatabase;
    [SerializeField] private ItemLinkDatabase linkDatabase;

    private void Awake()
    {
        if (heldDatabase == null)
        {
            Debug.LogError("Held Item Database is not assigned!");
            return;
        }
        heldDatabase.RegisterAsGlobalInstance();

        if (linkDatabase == null)
        {
            Debug.LogError("Link Database is not assigned!");
            return;
        }
        linkDatabase.RegisterAsGlobalInstance();
    }
}