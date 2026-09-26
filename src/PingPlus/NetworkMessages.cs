using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace PingPlus;

public sealed class PinnedPingRequestMessage : INetMessage
{
    public PinnedPingRequestMessage()
    {
    }

    public PinnedPingRequestMessage(
        NetworkInstanceId ownerId,
        NetworkInstanceId targetId,
        Vector3 origin,
        Vector3 normal,
        float duration,
        int maximum)
    {
        OwnerId = ownerId;
        TargetId = targetId;
        Origin = origin;
        Normal = normal;
        Duration = duration;
        Maximum = maximum;
    }

    public NetworkInstanceId OwnerId { get; private set; }
    public NetworkInstanceId TargetId { get; private set; }
    public Vector3 Origin { get; private set; }
    public Vector3 Normal { get; private set; }
    public float Duration { get; private set; }
    public int Maximum { get; private set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.Write(OwnerId);
        writer.Write(TargetId);
        writer.Write(Origin);
        writer.Write(Normal);
        writer.Write(Duration);
        writer.Write(Maximum);
    }

    public void Deserialize(NetworkReader reader)
    {
        OwnerId = reader.ReadNetworkId();
        TargetId = reader.ReadNetworkId();
        Origin = reader.ReadVector3();
        Normal = reader.ReadVector3();
        Duration = reader.ReadSingle();
        Maximum = reader.ReadInt32();
    }

    public void OnReceived()
    {
        Plugin.Instance.ReceivePinnedPingRequest(this);
    }
}

public enum PinnedPingOperation : byte
{
    Add,
    Remove,
    ClearOwner,
    ClearAll
}

public sealed class PinnedPingStateMessage : INetMessage
{
    public PinnedPingStateMessage()
    {
    }

    public PinnedPingStateMessage(
        PinnedPingOperation operation,
        NetworkInstanceId ownerId = default,
        NetworkInstanceId targetId = default,
        Vector3 origin = default,
        Vector3 normal = default,
        float duration = 0f,
        int labelIndex = -1)
    {
        Operation = operation;
        OwnerId = ownerId;
        TargetId = targetId;
        Origin = origin;
        Normal = normal;
        Duration = duration;
        LabelIndex = labelIndex;
    }

    public PinnedPingOperation Operation { get; private set; }
    public NetworkInstanceId OwnerId { get; private set; }
    public NetworkInstanceId TargetId { get; private set; }
    public Vector3 Origin { get; private set; }
    public Vector3 Normal { get; private set; }
    public float Duration { get; private set; }
    public int LabelIndex { get; private set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.Write((byte)Operation);
        writer.Write(OwnerId);
        writer.Write(TargetId);
        writer.Write(Origin);
        writer.Write(Normal);
        writer.Write(Duration);
        writer.Write(LabelIndex);
    }

    public void Deserialize(NetworkReader reader)
    {
        Operation = (PinnedPingOperation)reader.ReadByte();
        OwnerId = reader.ReadNetworkId();
        TargetId = reader.ReadNetworkId();
        Origin = reader.ReadVector3();
        Normal = reader.ReadVector3();
        Duration = reader.ReadSingle();
        LabelIndex = reader.ReadInt32();
    }

    public void OnReceived()
    {
        Plugin.Instance.ReceivePinnedPingState(this);
    }
}

public sealed class ClearPinnedPingsRequestMessage : INetMessage
{
    public ClearPinnedPingsRequestMessage()
    {
    }

    public ClearPinnedPingsRequestMessage(NetworkInstanceId ownerId)
    {
        OwnerId = ownerId;
    }

    public NetworkInstanceId OwnerId { get; private set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.Write(OwnerId);
    }

    public void Deserialize(NetworkReader reader)
    {
        OwnerId = reader.ReadNetworkId();
    }

    public void OnReceived()
    {
        Plugin.Instance.ReceiveClearPinnedPingsRequest(OwnerId);
    }
}

public sealed class ItemPingRequestMessage : INetMessage
{
    private int _pickupValue;

    public ItemPingRequestMessage()
    {
    }

    public ItemPingRequestMessage(PickupIndex pickupIndex)
    {
        _pickupValue = pickupIndex.value;
    }

    public void Serialize(NetworkWriter writer)
    {
        writer.Write(_pickupValue);
    }

    public void Deserialize(NetworkReader reader)
    {
        _pickupValue = reader.ReadInt32();
    }

    public void OnReceived()
    {
        if (NetworkServer.active)
            Plugin.Instance.BroadcastItemOwnership(new PickupIndex(_pickupValue));
    }
}
