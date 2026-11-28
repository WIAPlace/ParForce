using System;
using Unity.Netcode;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>Compact equipment entry using the stable NetworkSlotId authored on its EquipmentSlotDefinition.</summary>
    [Serializable]
    public struct NetworkEquipmentItemData : INetworkSerializable, IEquatable<NetworkEquipmentItemData>
    {
        public ushort DefinitionId;
        public uint InstanceId;
        public int Quantity;
        public ushort NetworkSlotId;

        public bool IsValid => DefinitionId != 0 && InstanceId != 0 && Quantity > 0 && NetworkSlotId != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref DefinitionId);
            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref Quantity);
            serializer.SerializeValue(ref NetworkSlotId);
        }

        public bool Equals(NetworkEquipmentItemData other)
        {
            return DefinitionId == other.DefinitionId && InstanceId == other.InstanceId &&
                Quantity == other.Quantity && NetworkSlotId == other.NetworkSlotId;
        }

        public override bool Equals(object obj) => obj is NetworkEquipmentItemData other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = DefinitionId;
                hash = (hash * 397) ^ InstanceId.GetHashCode();
                hash = (hash * 397) ^ Quantity;
                hash = (hash * 397) ^ NetworkSlotId;
                return hash;
            }
        }

        public static bool operator ==(NetworkEquipmentItemData left, NetworkEquipmentItemData right) => left.Equals(right);
        public static bool operator !=(NetworkEquipmentItemData left, NetworkEquipmentItemData right) => !left.Equals(right);
    }
}
