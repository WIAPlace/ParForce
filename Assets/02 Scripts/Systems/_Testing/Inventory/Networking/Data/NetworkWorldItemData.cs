using System;
using Unity.Netcode;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Compact, public description of an item currently existing in the shared world.
    /// The NetworkObjectId identifies this world instance; the item definition catalog
    /// identifies its authored definition. Private inventory GUIDs are never transmitted.
    /// </summary>
    [Serializable]
    public struct NetworkWorldItemData : INetworkSerializable, IEquatable<NetworkWorldItemData>
    {
        public ushort DefinitionId;
        public int Quantity;

        public bool IsValid => DefinitionId != 0 && Quantity > 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref DefinitionId);
            serializer.SerializeValue(ref Quantity);
        }

        public bool Equals(NetworkWorldItemData other)
        {
            return DefinitionId == other.DefinitionId && Quantity == other.Quantity;
        }

        public override bool Equals(object obj) => obj is NetworkWorldItemData other && Equals(other);
        public override int GetHashCode() => (DefinitionId * 397) ^ Quantity;

        public static bool operator ==(NetworkWorldItemData left, NetworkWorldItemData right) => left.Equals(right);
        public static bool operator !=(NetworkWorldItemData left, NetworkWorldItemData right) => !left.Equals(right);
    }
}
