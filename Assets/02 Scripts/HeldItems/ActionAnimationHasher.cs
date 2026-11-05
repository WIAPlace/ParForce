using UnityEngine;
using System.Collections.Generic;

public static class ActionAnimationHasher
{
    // Stores already calculated hashes to prevent re-hashing strings
    private static readonly Dictionary<string, int> HashCache = new Dictionary<string, int>();

    public static int GetTriggerHash(string name)
    {
        // If it's not in the dictionary yet, calculate and store it
        if (!HashCache.TryGetValue(name, out int hash))
        {
            hash = Animator.StringToHash(name);
            HashCache[name] = hash;
        }

        return hash;
    }
}