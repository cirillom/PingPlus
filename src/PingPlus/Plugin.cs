using BepInEx;
using BepInEx.Configuration;
using R2API.Networking;
using R2API.Networking.Interfaces;
using R2API.Utils;
using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.Networking;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace PingPlus;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(NetworkingAPI.PluginGUID)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.cirillom.pingplus";
    public const string PluginName = "Ping Plus";
    public const string PluginVersion = "1.0.1";

    internal static Plugin Instance { get; private set; } = null!;

    private readonly List<PinnedPing> _pinnedPings = [];
    private ConfigEntry<float> _duration = null!;
    private ConfigEntry<int> _maximum = null!;
    private ConfigEntry<KeyboardShortcut> _pinKey = null!;
    private ConfigEntry<KeyboardShortcut> _clearKey = null!;

    private void Awake()
    {
        Instance = this;

        _duration = Config.Bind(
            "Pinned Pings",
            "PinnedPingDuration",
            0f,
            new ConfigDescription("Pinned ping lifetime in seconds. Use 0 to keep pings until removed or the stage ends.", new AcceptableValueRange<float>(0f, 3600f)));
        _maximum = Config.Bind(
            "Pinned Pings",
            "MaxPinnedPings",
            5,
            new ConfigDescription("Maximum number of pinned pings per player. Creating another removes their oldest.", new AcceptableValueRange<int>(1, 20)));
        _pinKey = Config.Bind(
            "Pinned Pings",
            "PinKey",
            new KeyboardShortcut(KeyCode.G),
            "Key used to create or remove a pinned ping at the crosshair.");
        _clearKey = Config.Bind(
            "Pinned Pings",
            "ClearKey",
            new KeyboardShortcut(KeyCode.P),
            "Key used to remove every pinned ping for the whole party.");

        NetworkingAPI.RegisterMessageType<PinnedPingMessage>();
        NetworkingAPI.RegisterMessageType<ClearPinnedPingsMessage>();
        NetworkingAPI.RegisterMessageType<ItemPingRequestMessage>();
        On.RoR2.PlayerCharacterMasterController.Update += PlayerCharacterMasterControllerUpdate;
        On.RoR2.UI.PingIndicator.RebuildPing += PingIndicatorRebuildPing;
        Stage.onStageStartGlobal += _ => ClearPinnedPings();
        Logger.LogInfo($"Ping Plus loaded! Press {_pinKey.Value} to toggle a shared pinned ping or {_clearKey.Value} to clear all pinned pings.");
    }

    private void Update()
    {
        _pinnedPings.RemoveAll(ping => !ping.Indicator);
    }

    private void PlayerCharacterMasterControllerUpdate(
        On.RoR2.PlayerCharacterMasterController.orig_Update orig,
        PlayerCharacterMasterController self)
    {
        orig(self);

        if (!self.hasEffectiveAuthority)
            return;

        if (_clearKey.Value.IsDown())
        {
            if (NetworkServer.active)
                ReceiveClearPinnedPings(false);
            else
                new ClearPinnedPingsMessage(false).Send(NetworkDestination.Server);

            return;
        }

        if (!self.bodyInputs ||
            !self.body ||
            !_pinKey.Value.IsDown())
            return;

        var aimRay = new Ray(self.bodyInputs.aimOrigin, self.bodyInputs.aimDirection);

        if (!PingerController.GeneratePingInfo(aimRay, self.body.gameObject, out var pingInfo) ||
            !pingInfo.targetGameObject)
            return;

        var ownerId = GetNetworkId(self.gameObject);

        if (ownerId == default)
        {
            Logger.LogWarning("Could not pin ping because the local player has no network identity.");
            return;
        }

        var message = new PinnedPingMessage(
            ownerId,
            GetNetworkId(pingInfo.targetGameObject),
            pingInfo.origin,
            pingInfo.normal,
            _duration.Value,
            _maximum.Value,
            false);

        if (NetworkServer.active)
            ReceivePinnedPing(message);
        else
            message.Send(NetworkDestination.Server);
    }

    private static void PingIndicatorRebuildPing(
        On.RoR2.UI.PingIndicator.orig_RebuildPing orig,
        PingIndicator self)
    {
        orig(self);

        if (!self.pingOwner ||
            !self.pingTarget ||
            self.GetComponent<ItemPingReported>() ||
            !HasAuthority(self.pingOwner) ||
            !TryGetPickupIndex(self.pingTarget, out var pickupIndex))
            return;

        var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);

        if (pickupDef == null ||
            (pickupDef.itemIndex == ItemIndex.None && pickupDef.equipmentIndex == EquipmentIndex.None))
            return;

        self.gameObject.AddComponent<ItemPingReported>();

        if (NetworkServer.active)
            Instance.BroadcastItemOwnership(pickupIndex);
        else
            new ItemPingRequestMessage(pickupIndex).Send(NetworkDestination.Server);
    }

    internal void BroadcastItemOwnership(PickupIndex pickupIndex)
    {
        if (!NetworkServer.active || !pickupIndex.isValid)
            return;

        var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);

        if (pickupDef == null ||
            (pickupDef.itemIndex == ItemIndex.None && pickupDef.equipmentIndex == EquipmentIndex.None))
            return;

        var owners = NetworkUser.readOnlyInstancesList
            .Select(user => new
            {
                User = user,
                Count = GetCount(user.master?.inventory, pickupDef)
            })
            .Where(entry => entry.Count > 0)
            .Select(entry => $"{Util.EscapeRichTextForTextMeshPro(entry.User.userName)} ×{entry.Count}")
            .ToArray();

        if (owners.Length == 0)
            return;

        Chat.SendBroadcastChat(new Chat.SimpleChatMessage
        {
            baseToken = $"<style=cSub>Owned by: {string.Join(", ", owners)}</style>"
        });
    }

    internal void ReceivePinnedPing(PinnedPingMessage message)
    {
        if (message.IsBroadcast)
        {
            if (!NetworkServer.active)
                ApplyPinnedPing(message);

            return;
        }

        if (!NetworkServer.active)
            return;

        var broadcast = new PinnedPingMessage(
            message.OwnerId,
            message.TargetId,
            message.Origin,
            message.Normal,
            Mathf.Clamp(message.Duration, 0f, 3600f),
            Mathf.Clamp(message.Maximum, 1, 20),
            true);

        if (NetworkClient.active)
            ApplyPinnedPing(broadcast);

        broadcast.Send(NetworkDestination.Clients);
    }

    internal void ReceiveClearPinnedPings(bool isBroadcast)
    {
        if (isBroadcast)
        {
            if (!NetworkServer.active)
                ClearPinnedPings();

            return;
        }

        if (!NetworkServer.active)
            return;

        if (NetworkClient.active)
            ClearPinnedPings();

        new ClearPinnedPingsMessage(true).Send(NetworkDestination.Clients);
    }

    private void ApplyPinnedPing(PinnedPingMessage message)
    {
        var owner = Util.FindNetworkObject(message.OwnerId);

        if (!owner)
        {
            Logger.LogWarning($"Could not resolve pinned ping owner {message.OwnerId}.");
            return;
        }

        var target = message.TargetId == default ? null : Util.FindNetworkObject(message.TargetId);
        var existing = target
            ? _pinnedPings.FindIndex(ping => ping.Owner == owner && ping.Target == target)
            : -1;

        if (existing >= 0)
        {
            Destroy(_pinnedPings[existing].Indicator.gameObject);
            _pinnedPings.RemoveAt(existing);
            return;
        }

        while (_pinnedPings.Count(ping => ping.Owner == owner) >= message.Maximum)
        {
            var oldest = _pinnedPings.FindIndex(ping => ping.Owner == owner);
            Destroy(_pinnedPings[oldest].Indicator.gameObject);
            _pinnedPings.RemoveAt(oldest);
        }

        var prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/PingIndicator");

        if (!prefab)
        {
            Logger.LogWarning("Could not load the vanilla ping indicator prefab.");
            return;
        }

        var indicator = Instantiate(prefab).GetComponent<PingIndicator>();
        indicator.pingOwner = owner;
        indicator.pingOrigin = message.Origin;
        indicator.pingNormal = message.Normal;
        indicator.pingTarget = target;
        indicator.RebuildPing();

        var lifetime = message.Duration == 0f ? float.PositiveInfinity : message.Duration;
        indicator.pingDuration = lifetime;
        indicator.fixedTimer = lifetime;
        indicator.pingText.text = $"<color=#80E9FF><b>PINNED</b></color>\n{indicator.pingText.text}";

        foreach (var sprite in indicator.GetComponentsInChildren<SpriteRenderer>(true))
            sprite.color = Color.Lerp(sprite.color, new Color(0.35f, 0.90f, 1f, sprite.color.a), 0.55f);

        _pinnedPings.Add(new PinnedPing(owner, target, indicator));
    }

    private static bool HasAuthority(GameObject owner)
    {
        var identity = owner.GetComponentInParent<NetworkIdentity>();
        return identity && identity.hasAuthority;
    }

    private static NetworkInstanceId GetNetworkId(GameObject? value)
    {
        if (value == null)
            return default;

        var identity = value.GetComponentInParent<NetworkIdentity>();
        return identity ? identity.netId : default;
    }

    private static bool TryGetPickupIndex(GameObject target, out PickupIndex pickupIndex)
    {
        var pickup = target.GetComponentInParent<GenericPickupController>() ??
                     target.GetComponentInChildren<GenericPickupController>();

        if (pickup)
        {
            pickupIndex = pickup.pickup.pickupIndex;
            return pickupIndex.isValid;
        }

        var display = target.GetComponentInChildren<PickupDisplay>();
        pickupIndex = display ? display.GetPickupIndex() : PickupIndex.none;
        return pickupIndex.isValid;
    }

    private static int GetCount(Inventory? inventory, PickupDef pickupDef)
    {
        if (inventory == null)
            return 0;

        if (pickupDef.itemIndex != ItemIndex.None)
            return inventory.GetItemCountEffective(pickupDef.itemIndex);

        return pickupDef.equipmentIndex != EquipmentIndex.None && inventory.HasEquipment(pickupDef.equipmentIndex)
            ? 1
            : 0;
    }

    private void ClearPinnedPings()
    {
        foreach (var ping in _pinnedPings)
        {
            if (ping.Indicator)
                Destroy(ping.Indicator.gameObject);
        }

        _pinnedPings.Clear();
    }

    private sealed class ItemPingReported : MonoBehaviour
    {
    }

    private sealed class PinnedPing
    {
        public PinnedPing(GameObject owner, GameObject? target, PingIndicator indicator)
        {
            Owner = owner;
            Target = target;
            Indicator = indicator;
        }

        public GameObject Owner { get; }
        public GameObject? Target { get; }
        public PingIndicator Indicator { get; }
    }
}
