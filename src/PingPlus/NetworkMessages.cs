using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace PingPlus;

public sealed class PinnedPingMessage : INetMessage
{
    public PinnedPingMessage()
    {
    }

    public PinnedPingMessage(
        NetworkInstanceId ownerId,
        NetworkInstanceId targetId,
        Vector3 origin,
        Vector3 normal,
        float duration,
        int maximum,
        bool isBroadcast)
    {
        OwnerId = ownerId;
        TargetId = targetId;
        Origin = origin;
        Normal = normal;
        Duration = duration;
        Maximum = maximum;
        IsBroadcast = isBroadcast;
    }

    public NetworkInstanceId OwnerId { get; private set; }
    public NetworkInstanceId TargetId { get; private set; }
    public Vector3 Origin { get; private set; }
    public Vector3 Normal { get; private set; }
    public float Duration { get; private set; }
    public int Maximum { get; private set; }
    public bool IsBroadcast { get; private set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.Write(OwnerId);
        writer.Write(TargetId);
        writer.Write(Origin);
        writer.Write(Normal);
        writer.Write(Duration);
        writer.Write(Maximum);
        writer.Write(IsBroadcast);
    }

    public void Deserialize(NetworkReader reader)
    {
        OwnerId = reader.ReadNetworkId();
        TargetId = reader.ReadNetworkId();
        Origin = reader.ReadVector3();
        Normal = reader.ReadVector3();
        Duration = reader.ReadSingle();
        Maximum = reader.ReadInt32();
        IsBroadcast = reader.ReadBoolean();
    }

    public void OnReceived()
    {
        Plugin.Instance.ReceivePinnedPing(this);
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
