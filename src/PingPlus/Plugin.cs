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
    private const int MaximumPlayers = 4;
    private const int SlotsPerPlayer = 5;

    public const string PluginGuid = "com.cirillom.pingplus";
    public const string PluginName = "Ping Plus";
    public const string PluginVersion = "1.2.1";

    internal static Plugin Instance { get; private set; } = null!;

    private readonly List<PinnedPing> _pinnedPings = [];
    private readonly List<ServerPinnedPing> _serverPinnedPings = [];
    private readonly Dictionary<NetworkInstanceId, int> _playerBlocks = [];
    private ConfigEntry<float> _duration = null!;
    private ConfigEntry<int> _maximum = null!;
    private ConfigEntry<KeyboardShortcut> _pinKey = null!;
    private ConfigEntry<KeyboardShortcut> _clearKey = null!;
    private ConfigEntry<bool> _showDistance = null!;

    private void Awake()
    {
        Instance = this;
        PingAppearance.Initialize(Logger);

        _duration = Config.Bind(
            "Pinned Pings",
            "PinnedPingDuration",
            0f,
            new ConfigDescription("Pinned ping lifetime in seconds. Use 0 to keep pings until removed or the stage ends.", new AcceptableValueRange<float>(0f, 3600f)));
        _maximum = Config.Bind(
            "Pinned Pings",
            "MaxPinnedPings",
            5,
            new ConfigDescription("Maximum number of pinned pings per player. Creating another removes their oldest.", new AcceptableValueRange<int>(1, SlotsPerPlayer)));
        _pinKey = Config.Bind(
            "Pinned Pings",
            "PinKey",
            new KeyboardShortcut(KeyCode.G),
            "Key used to create or remove a pinned ping at the crosshair.");
        _clearKey = Config.Bind(
            "Pinned Pings",
            "ClearKey",
            new KeyboardShortcut(KeyCode.P),
            "Key used to remove only the pinned pings you created.");
        _showDistance = Config.Bind(
            "Ping Display",
            "ShowDistance",
            true,
            "Show the local player's distance to normal and pinned pings.");

        NetworkingAPI.RegisterMessageType<PinnedPingRequestMessage>();
        NetworkingAPI.RegisterMessageType<PinnedPingStateMessage>();
        NetworkingAPI.RegisterMessageType<ClearPinnedPingsRequestMessage>();
        NetworkingAPI.RegisterMessageType<ItemPingRequestMessage>();
        On.RoR2.PlayerCharacterMasterController.Update += PlayerCharacterMasterControllerUpdate;
        On.RoR2.PositionIndicator.UpdatePositions += PositionIndicatorUpdatePositions;
        On.RoR2.UI.PingIndicator.RebuildPing += PingIndicatorRebuildPing;
        On.RoR2.UI.PingIndicator.Update += PingIndicatorUpdate;
        Stage.onStageStartGlobal += OnStageStart;
        NetworkUser.onPostNetworkUserStart += OnPostNetworkUserStart;
        Logger.LogInfo($"Ping Plus loaded! Press {_pinKey.Value} to toggle a shared pinned ping or {_clearKey.Value} to clear your pinned pings.");
    }

    private void Update()
    {
        _pinnedPings.RemoveAll(ping => !ping.Indicator);

        if (NetworkServer.active)
            UpdateServerPinnedPings();
    }

    private void PlayerCharacterMasterControllerUpdate(
        On.RoR2.PlayerCharacterMasterController.orig_Update orig,
        PlayerCharacterMasterController self)
    {
        orig(self);

        if (!self.hasEffectiveAuthority)
            return;

        if (IsShortcutDown(_clearKey.Value))
        {
            var clearOwnerId = GetNetworkId(self.gameObject);

            if (clearOwnerId == default)
            {
                Logger.LogWarning("Could not clear pinned pings because the local player has no network identity.");
                return;
            }

            if (NetworkServer.active)
                ReceiveClearPinnedPingsRequest(clearOwnerId);
            else
                new ClearPinnedPingsRequestMessage(clearOwnerId).Send(NetworkDestination.Server);

            return;
        }

        if (!self.bodyInputs ||
            !self.body ||
            !IsShortcutDown(_pinKey.Value))
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

        ClearNormalPingForTarget(self, pingInfo.targetGameObject);

        var message = new PinnedPingRequestMessage(
            ownerId,
            GetNetworkId(pingInfo.targetGameObject),
            pingInfo.origin,
            pingInfo.normal,
            _duration.Value,
            _maximum.Value);

        if (NetworkServer.active)
            ReceivePinnedPingRequest(message);
        else
            message.Send(NetworkDestination.Server);
    }

    private static void PingIndicatorRebuildPing(
        On.RoR2.UI.PingIndicator.orig_RebuildPing orig,
        PingIndicator self)
    {
        orig(self);
        PingAppearance.TryApply(self);

        if (!self.pingOwner ||
            !self.pingTarget ||
            self.GetComponent<ItemPingReported>() ||
            !HasAuthority(self.pingOwner) ||
            !PingAppearance.TryGetPickupIndex(self.pingTarget, out var pickupIndex))
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

    private static void PingIndicatorUpdate(
        On.RoR2.UI.PingIndicator.orig_Update orig,
        PingIndicator self)
    {
        orig(self);

        if (!self)
            return;

        PingDistance.Update(self, Instance._showDistance.Value);
        PingAppearance.TryApply(self);
        PingAppearance.SyncTextColorToIcon(self);
        PingLayout.Update(self);
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

    internal void ReceivePinnedPingRequest(PinnedPingRequestMessage message)
    {
        if (!NetworkServer.active ||
            !IsPlayerOwner(message.OwnerId) ||
            message.TargetId == default ||
            !Util.FindNetworkObject(message.TargetId))
            return;

        var existing = _serverPinnedPings.FindIndex(
            ping => ping.OwnerId == message.OwnerId && ping.TargetId == message.TargetId);

        if (existing >= 0)
        {
            RemoveServerPinnedPingAt(existing);
            return;
        }

        var playerBlock = GetOrAssignPlayerBlock(message.OwnerId);

        if (playerBlock < 0)
        {
            Logger.LogWarning($"Could not pin target for owner {message.OwnerId}: all {MaximumPlayers} player label blocks are in use.");
            return;
        }

        var maximum = Mathf.Clamp(message.Maximum, 1, SlotsPerPlayer);

        while (_serverPinnedPings.Count(ping => ping.OwnerId == message.OwnerId) >= maximum)
        {
            var oldest = _serverPinnedPings.FindIndex(ping => ping.OwnerId == message.OwnerId);
            RemoveServerPinnedPingAt(oldest);
        }

        var localSlot = FindAvailableLocalSlot(message.OwnerId);
        var duration = Mathf.Clamp(message.Duration, 0f, 3600f);
        var state = new ServerPinnedPing(
            message.OwnerId,
            message.TargetId,
            message.Origin,
            message.Normal,
            playerBlock * SlotsPerPlayer + localSlot,
            duration == 0f ? float.PositiveInfinity : Time.time + duration);
        _serverPinnedPings.Add(state);
        BroadcastPinnedPingState(CreateAddMessage(state, duration));
    }

    internal void ReceivePinnedPingState(PinnedPingStateMessage message)
    {
        if (!NetworkServer.active)
            ApplyPinnedPingState(message);
    }

    internal void ReceiveClearPinnedPingsRequest(NetworkInstanceId ownerId)
    {
        if (!NetworkServer.active || !IsPlayerOwner(ownerId))
            return;

        ClearServerPinnedPings(ownerId, true);
    }

    private void ApplyPinnedPingState(PinnedPingStateMessage message)
    {
        switch (message.Operation)
        {
            case PinnedPingOperation.Add:
                ApplyPinnedPing(message);
                break;
            case PinnedPingOperation.Remove:
                RemoveRenderedPinnedPing(message.OwnerId, message.LabelIndex);
                break;
            case PinnedPingOperation.ClearOwner:
                ClearRenderedPinnedPings(message.OwnerId);
                break;
            case PinnedPingOperation.ClearAll:
                ClearRenderedPinnedPings();
                break;
        }
    }

    private void ApplyPinnedPing(PinnedPingStateMessage message)
    {
        var owner = Util.FindNetworkObject(message.OwnerId);
        var target = Util.FindNetworkObject(message.TargetId);

        if (!owner || !target)
        {
            Logger.LogWarning($"Could not resolve pinned ping owner {message.OwnerId} or target {message.TargetId}.");
            return;
        }

        RemoveRenderedPinnedPings(
            ping => ping.OwnerId == message.OwnerId &&
                    (ping.TargetId == message.TargetId || ping.LabelIndex == message.LabelIndex));

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
        PingAppearance.TryApply(indicator);
        var label = PinnedPing.GetDisplayName(message.LabelIndex);
        indicator.pingText.text = $"<b>{label}</b>";
        PingAppearance.SyncTextColorToIcon(indicator);
        PingLayout.Update(indicator);

        _pinnedPings.Add(new PinnedPing(message.OwnerId, message.TargetId, indicator, message.LabelIndex));
    }

    private static bool HasAuthority(GameObject owner)
    {
        var identity = owner.GetComponentInParent<NetworkIdentity>();
        return identity && identity.hasAuthority;
    }

    private static void PositionIndicatorUpdatePositions(
        On.RoR2.PositionIndicator.orig_UpdatePositions orig,
        UICamera uiCamera)
    {
        orig(uiCamera);

        if (!uiCamera || !uiCamera.camera)
            return;

        for (var index = 0; index < PingIndicator.instancesList.Count; index++)
        {
            var indicator = PingIndicator.instancesList[index];

            if (indicator)
                PingLayout.ClampToViewport(indicator, uiCamera.camera);
        }
    }

    private static void ClearNormalPingForTarget(PlayerCharacterMasterController controller, GameObject target)
    {
        var pinger = controller.GetComponent<PingerController>();

        if (pinger && pinger.currentPing.targetGameObject == target)
            pinger.SetCurrentPing(PingerController.emptyPing);
    }

    private static bool IsShortcutDown(KeyboardShortcut shortcut)
    {
        return shortcut.MainKey != KeyCode.None &&
               Input.GetKeyDown(shortcut.MainKey) &&
               shortcut.Modifiers.All(Input.GetKey);
    }

    private static NetworkInstanceId GetNetworkId(GameObject? value)
    {
        if (value == null)
            return default;

        var identity = value.GetComponentInParent<NetworkIdentity>();
        return identity ? identity.netId : default;
    }

    private static bool IsPlayerOwner(NetworkInstanceId ownerId)
    {
        var owner = Util.FindNetworkObject(ownerId);
        return owner && owner.GetComponentInParent<PlayerCharacterMasterController>();
    }

    private int GetOrAssignPlayerBlock(NetworkInstanceId ownerId)
    {
        if (_playerBlocks.TryGetValue(ownerId, out var playerBlock))
            return playerBlock;

        var usedBlocks = _playerBlocks.Values.ToHashSet();

        for (playerBlock = 0; playerBlock < MaximumPlayers; playerBlock++)
        {
            if (usedBlocks.Contains(playerBlock))
                continue;

            _playerBlocks.Add(ownerId, playerBlock);
            return playerBlock;
        }

        return -1;
    }

    private int FindAvailableLocalSlot(NetworkInstanceId ownerId)
    {
        var usedSlots = _serverPinnedPings
            .Where(ping => ping.OwnerId == ownerId)
            .Select(ping => ping.LabelIndex % SlotsPerPlayer)
            .ToHashSet();

        for (var localSlot = 0; localSlot < SlotsPerPlayer; localSlot++)
        {
            if (!usedSlots.Contains(localSlot))
                return localSlot;
        }

        return 0;
    }

    private void UpdateServerPinnedPings()
    {
        var disconnectedOwners = _playerBlocks.Keys
            .Where(ownerId => !IsPlayerOwner(ownerId))
            .ToArray();

        foreach (var ownerId in disconnectedOwners)
        {
            ClearServerPinnedPings(ownerId, true);
            _playerBlocks.Remove(ownerId);
        }

        for (var index = _serverPinnedPings.Count - 1; index >= 0; index--)
        {
            var ping = _serverPinnedPings[index];

            if (!Util.FindNetworkObject(ping.TargetId) || Time.time >= ping.ExpiresAt)
                RemoveServerPinnedPingAt(index);
        }
    }

    private void RemoveServerPinnedPingAt(int index)
    {
        var ping = _serverPinnedPings[index];
        _serverPinnedPings.RemoveAt(index);
        BroadcastPinnedPingState(new PinnedPingStateMessage(
            PinnedPingOperation.Remove,
            ping.OwnerId,
            labelIndex: ping.LabelIndex));
    }

    private void ClearServerPinnedPings(NetworkInstanceId ownerId, bool broadcast)
    {
        _serverPinnedPings.RemoveAll(ping => ping.OwnerId == ownerId);

        if (broadcast)
            BroadcastPinnedPingState(new PinnedPingStateMessage(PinnedPingOperation.ClearOwner, ownerId));
    }

    private void BroadcastPinnedPingState(PinnedPingStateMessage message)
    {
        if (NetworkClient.active)
            ApplyPinnedPingState(message);

        message.Send(NetworkDestination.Clients);
    }

    private static PinnedPingStateMessage CreateAddMessage(ServerPinnedPing ping, float duration)
    {
        return new PinnedPingStateMessage(
            PinnedPingOperation.Add,
            ping.OwnerId,
            ping.TargetId,
            ping.Origin,
            ping.Normal,
            duration,
            ping.LabelIndex);
    }

    private void OnPostNetworkUserStart(NetworkUser networkUser)
    {
        if (!NetworkServer.active || networkUser.connectionToClient == null)
            return;

        new PinnedPingStateMessage(PinnedPingOperation.ClearAll).Send(networkUser.connectionToClient);

        foreach (var ping in _serverPinnedPings)
        {
            var hasInfiniteDuration = float.IsPositiveInfinity(ping.ExpiresAt);
            var remainingDuration = hasInfiniteDuration ? 0f : ping.ExpiresAt - Time.time;

            if (!hasInfiniteDuration && remainingDuration <= 0f)
                continue;

            CreateAddMessage(ping, remainingDuration).Send(networkUser.connectionToClient);
        }
    }

    private void OnStageStart(Stage _)
    {
        if (NetworkServer.active)
        {
            _serverPinnedPings.Clear();
            BroadcastPinnedPingState(new PinnedPingStateMessage(PinnedPingOperation.ClearAll));
        }
        else
        {
            ClearRenderedPinnedPings();
        }
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

    private void RemoveRenderedPinnedPing(NetworkInstanceId ownerId, int labelIndex)
    {
        RemoveRenderedPinnedPings(ping => ping.OwnerId == ownerId && ping.LabelIndex == labelIndex);
    }

    private void ClearRenderedPinnedPings(NetworkInstanceId ownerId)
    {
        RemoveRenderedPinnedPings(ping => ping.OwnerId == ownerId);
    }

    private void RemoveRenderedPinnedPings(System.Predicate<PinnedPing> predicate)
    {
        for (var index = _pinnedPings.Count - 1; index >= 0; index--)
        {
            var ping = _pinnedPings[index];

            if (!predicate(ping))
                continue;

            if (ping.Indicator)
                Destroy(ping.Indicator.gameObject);

            _pinnedPings.RemoveAt(index);
        }
    }

    private void ClearRenderedPinnedPings()
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

    private sealed class ServerPinnedPing(
        NetworkInstanceId ownerId,
        NetworkInstanceId targetId,
        Vector3 origin,
        Vector3 normal,
        int labelIndex,
        float expiresAt)
    {
        public NetworkInstanceId OwnerId { get; } = ownerId;
        public NetworkInstanceId TargetId { get; } = targetId;
        public Vector3 Origin { get; } = origin;
        public Vector3 Normal { get; } = normal;
        public int LabelIndex { get; } = labelIndex;
        public float ExpiresAt { get; } = expiresAt;
    }

}
