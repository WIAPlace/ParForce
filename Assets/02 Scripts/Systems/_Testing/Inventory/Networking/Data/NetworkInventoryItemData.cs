using System;
using Unity.Netcode;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>Compact grid item entry. No SerializableGuid or string is sent.</summary>
    [Serializable]
    public struct NetworkInventoryItemData : INetworkSerializable, IEquatable<NetworkInventoryItemData>
    {
        public ushort DefinitionId;
        public uint InstanceId;
        public int Quantity;
        public int X;
        public int Y;
        public byte RotationQuarterTurns;

        public bool IsValid => DefinitionId != 0 && InstanceId != 0 && Quantity > 0 &&
            X >= 0 && Y >= 0 && RotationQuarterTurns <= 3;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref DefinitionId);
            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref Quantity);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref RotationQuarterTurns);
        }

        public bool Equals(NetworkInventoryItemData other)
        {
            return DefinitionId == other.DefinitionId && InstanceId == other.InstanceId &&
                Quantity == other.Quantity && X == other.X && Y == other.Y &&
                RotationQuarterTurns == other.RotationQuarterTurns;
        }

        public override bool Equals(object obj) => obj is NetworkInventoryItemData other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = DefinitionId;
                hash = (hash * 397) ^ InstanceId.GetHashCode();
                hash = (hash * 397) ^ Quantity;
                hash = (hash * 397) ^ X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ RotationQuarterTurns;
                return hash;
            }
        }

        public static bool operator ==(NetworkInventoryItemData left, NetworkInventoryItemData right) => left.Equals(right);
        public static bool operator !=(NetworkInventoryItemData left, NetworkInventoryItemData right) => !left.Equals(right);
    }
}
