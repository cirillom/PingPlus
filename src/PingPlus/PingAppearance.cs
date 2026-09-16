using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PingPlus;

internal static class PingAppearance
{
    private const string ScrapIconResourceName = "PingPlus.Assets.scrap.png";

    private static readonly ConditionalWeakTable<SpriteRenderer, RendererState> RendererStates = new();
    private static Texture2D? _scrapTexture;
    private static Sprite? _scrapSprite;

    public static void Initialize(ManualLogSource logger)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScrapIconResourceName);

        if (stream == null)
        {
            logger.LogWarning($"Could not find embedded scrap ping icon {ScrapIconResourceName}.");
            return;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "PingPlusScrapIcon",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        // Sprite.Create needs the texture to remain readable until the sprite has been created.
        if (!ImageConversion.LoadImage(texture, buffer.ToArray(), false))
        {
            UnityEngine.Object.Destroy(texture);
            logger.LogWarning("Could not decode the embedded scrap ping icon.");
            return;
        }

        _scrapTexture = texture;
    }

    public static bool TryApply(PingIndicator indicator)
    {
        RestoreCustomizedRenderers(indicator.interactablePingGameObjects);

        if (!indicator.pingTarget || !TryGetAppearance(indicator.pingTarget, out var color, out var useScrapIcon))
            return false;

        SpriteRenderer? iconRenderer = null;

        foreach (var gameObject in indicator.interactablePingGameObjects)
        {
            if (!gameObject)
                continue;

            iconRenderer ??= gameObject.GetComponent<SpriteRenderer>();

            foreach (var spriteRenderer in gameObject.GetComponentsInChildren<SpriteRenderer>(true))
            {
                iconRenderer ??= spriteRenderer;
                if (color.HasValue)
                {
                    var state = GetRendererState(spriteRenderer);
                    var iconColor = color.Value;
                    iconColor.a = spriteRenderer.color.a;
                    spriteRenderer.color = iconColor;
                    state.HasCustomColor = true;
                    state.CustomColor = iconColor;
                }
            }
        }

        if (useScrapIcon && iconRenderer != null)
        {
            var scrapSprite = GetScrapSprite(iconRenderer.sprite);

            if (scrapSprite)
            {
                var state = GetRendererState(iconRenderer);
                state.HasCustomSprite = true;
                state.CustomSprite = scrapSprite;
                iconRenderer.sprite = scrapSprite;
            }
        }

        return true;
    }

    public static void SyncTextColorToIcon(PingIndicator indicator)
    {
        if (!indicator.pingText)
            return;

        var iconRenderer = FindIconRenderer(indicator.defaultPingGameObjects, true) ??
                           FindIconRenderer(indicator.enemyPingGameObjects, true) ??
                           FindIconRenderer(indicator.interactablePingGameObjects, true) ??
                           FindIconRenderer(indicator.defaultPingGameObjects, false) ??
                           FindIconRenderer(indicator.enemyPingGameObjects, false) ??
                           FindIconRenderer(indicator.interactablePingGameObjects, false);

        if (iconRenderer == null)
            return;

        indicator.pingText.overrideColorTags = true;
        indicator.pingText.color = iconRenderer.color;
    }

    public static bool TryGetPickupIndex(GameObject target, out PickupIndex pickupIndex)
    {
        if (IsScrapper(target) || IsShrine(target) || TryGetChestColor(target, out _))
        {
            pickupIndex = PickupIndex.none;
            return false;
        }

        var pickup = target.GetComponentInParent<GenericPickupController>();

        if (pickup)
        {
            pickupIndex = pickup.pickup.pickupIndex;
            return pickupIndex.isValid;
        }

        var shop = target.GetComponentInParent<ShopTerminalBehavior>();

        if (shop)
        {
            pickupIndex = shop.pickupIndexIsHidden
                ? PickupIndex.none
                : shop.CurrentPickup().pickupIndex;
            return pickupIndex.isValid;
        }

        var networker = target.GetComponentInParent<PickupIndexNetworker>();

        if (networker)
        {
            pickupIndex = networker.pickupState.pickupIndex;
            return pickupIndex.isValid;
        }

        var display = target.GetComponentInParent<PickupDisplay>();
        pickupIndex = display ? display.GetPickupIndex() : PickupIndex.none;
        return pickupIndex.isValid;
    }

    private static bool TryGetAppearance(GameObject target, out Color? color, out bool useScrapIcon)
    {
        if (IsScrapper(target))
        {
            color = null;
            useScrapIcon = true;
            return true;
        }

        if (IsNewtAltar(target))
        {
            color = GetColor(ColorCatalog.ColorIndex.LunarItem);
            useScrapIcon = false;
            return true;
        }

        if (TryGetHiddenShopColor(target, out var hiddenShopColor))
        {
            color = hiddenShopColor;
            useScrapIcon = false;
            return true;
        }

        if (TryGetChestColor(target, out var chestColor))
        {
            color = chestColor;
            useScrapIcon = false;
            return true;
        }

        if (IsShrine(target))
        {
            color = null;
            useScrapIcon = false;
            return false;
        }

        if (TryGetPickupIndex(target, out var pickupIndex))
        {
            var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);

            if (pickupDef != null && TryGetPickupAppearance(pickupDef, out var pickupColor, out useScrapIcon))
            {
                color = pickupColor;
                return true;
            }
        }

        useScrapIcon = false;
        color = null;
        return false;
    }

    private static bool TryGetHiddenShopColor(GameObject target, out Color color)
    {
        var shop = target.GetComponentInParent<ShopTerminalBehavior>();

        if (!shop || !shop.pickupIndexIsHidden)
        {
            color = default;
            return false;
        }

        var pickupIndex = shop.CurrentPickup().pickupIndex;
        var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);

        if (pickupDef != null && TryGetPickupAppearance(pickupDef, out color, out _))
            return true;

        var name = GetInteractableName(target);

        if (name.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier2Item);
            return true;
        }

        if (name.IndexOf("Equipment", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = GetColor(ColorCatalog.ColorIndex.Equipment);
            return true;
        }

        if (name.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.StartsWith("FreeChestTerminal", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier1Item);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryGetPickupAppearance(PickupDef pickupDef, out Color color, out bool isScrap)
    {
        var itemDef = pickupDef.itemIndex != ItemIndex.None
            ? ItemCatalog.GetItemDef(pickupDef.itemIndex)
            : null;
        isScrap = itemDef != null &&
                  (itemDef.ContainsTag(ItemTag.Scrap) || itemDef.ContainsTag(ItemTag.PriorityScrap));

        if (pickupDef.isLunar || itemDef?.tier == ItemTier.Lunar)
        {
            color = GetColor(ColorCatalog.ColorIndex.LunarItem);
            return true;
        }

        if (pickupDef.equipmentIndex != EquipmentIndex.None)
        {
            color = GetColor(ColorCatalog.ColorIndex.Equipment);
            return true;
        }

        switch (itemDef?.tier)
        {
            case ItemTier.Tier1:
                color = GetColor(ColorCatalog.ColorIndex.Tier1Item);
                return true;
            case ItemTier.Tier2:
                color = GetColor(ColorCatalog.ColorIndex.Tier2Item);
                return true;
            case ItemTier.Tier3:
                color = GetColor(ColorCatalog.ColorIndex.Tier3Item);
                return true;
            case ItemTier.Boss:
                color = GetColor(ColorCatalog.ColorIndex.BossItem);
                return true;
            case ItemTier.VoidTier1:
            case ItemTier.VoidTier2:
            case ItemTier.VoidTier3:
                color = GetColor(ColorCatalog.ColorIndex.VoidItem);
                return true;
            default:
                if (itemDef != null)
                {
                    color = pickupDef.baseColor;
                    return true;
                }

                color = default;
                return false;
        }
    }

    private static bool TryGetChestColor(GameObject target, out Color color)
    {
        var name = GetInteractableName(target);

        if (name.StartsWith("GoldChest", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier3Item);
            return true;
        }

        if (name.StartsWith("EquipmentBarrel", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Equipment);
            return true;
        }

        if (name.StartsWith("LunarChest", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("LunarPod", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.LunarItem);
            return true;
        }

        if (name.StartsWith("VoidChest", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("LockboxVoid", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.VoidItem);
            return true;
        }

        if (name.StartsWith("CategoryChest2", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier2Item);
            return true;
        }

        if (name.StartsWith("CategoryChest", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier1Item);
            return true;
        }

        if (name.StartsWith("Chest2", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier2Item);
            return true;
        }

        if (name.StartsWith("Chest1", StringComparison.OrdinalIgnoreCase))
        {
            color = GetColor(ColorCatalog.ColorIndex.Tier1Item);
            return true;
        }

        color = default;
        return false;
    }

    private static bool IsScrapper(GameObject target)
    {
        var scrapper = target.GetComponentInParent<ScrapperController>() ??
                       target.GetComponentInChildren<ScrapperController>();
        return scrapper || GetInteractableName(target).StartsWith("Scrapper", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNewtAltar(GameObject target)
    {
        var name = GetInteractableName(target);
        return name.StartsWith("NewtStatue", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("NewtAltar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShrine(GameObject target)
    {
        return GetInteractableName(target).IndexOf("Shrine", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static SpriteRenderer? FindIconRenderer(GameObject[] gameObjects, bool requireActive)
    {
        foreach (var gameObject in gameObjects)
        {
            if (!gameObject || (requireActive && !gameObject.activeInHierarchy))
                continue;

            var spriteRenderer = gameObject.GetComponent<SpriteRenderer>() ??
                                 gameObject.GetComponentInChildren<SpriteRenderer>(true);

            if (spriteRenderer)
                return spriteRenderer;
        }

        return null;
    }

    private static RendererState GetRendererState(SpriteRenderer renderer)
    {
        return RendererStates.GetValue(
            renderer,
            static value => new RendererState(value.color, value.sprite));
    }

    private static void RestoreCustomizedRenderers(GameObject[] gameObjects)
    {
        foreach (var gameObject in gameObjects)
        {
            if (!gameObject)
                continue;

            foreach (var spriteRenderer in gameObject.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!RendererStates.TryGetValue(spriteRenderer, out var state))
                    continue;

                if (state.HasCustomColor)
                {
                    if (HasSameRgb(spriteRenderer.color, state.CustomColor))
                    {
                        var baseColor = state.BaseColor;
                        baseColor.a = spriteRenderer.color.a;
                        spriteRenderer.color = baseColor;
                    }
                    else
                    {
                        state.BaseColor = spriteRenderer.color;
                    }

                    state.HasCustomColor = false;
                }

                if (state.HasCustomSprite)
                {
                    if (spriteRenderer.sprite == state.CustomSprite)
                        spriteRenderer.sprite = state.BaseSprite;
                    else
                        state.BaseSprite = spriteRenderer.sprite;

                    state.HasCustomSprite = false;
                }
            }
        }
    }

    private static bool HasSameRgb(Color left, Color right)
    {
        return Mathf.Approximately(left.r, right.r) &&
               Mathf.Approximately(left.g, right.g) &&
               Mathf.Approximately(left.b, right.b);
    }

    private static string GetInteractableName(GameObject target)
    {
        var interactable = target.GetComponentInParent<ChestBehavior>()?.gameObject ??
                           target.GetComponentInParent<PurchaseInteraction>()?.gameObject ??
                           target;
        return interactable.name;
    }

    private static Color GetColor(ColorCatalog.ColorIndex colorIndex)
    {
        Color color = ColorCatalog.GetColor(colorIndex);
        color.a = 1f;
        return color;
    }

    private static Sprite? GetScrapSprite(Sprite? originalSprite)
    {
        var texture = _scrapTexture;

        if (_scrapSprite || texture == null)
            return _scrapSprite;

        var displayWidth = originalSprite != null && originalSprite.bounds.size.x > 0f
            ? originalSprite.bounds.size.x
            : 1f;
        var pixelsPerUnit = texture.width / displayWidth;
        _scrapSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit);
        _scrapSprite.name = "PingPlusScrapIconSprite";
        _scrapSprite.hideFlags = HideFlags.HideAndDontSave;
        texture.Apply(false, true);
        return _scrapSprite;
    }

    private sealed class RendererState(Color baseColor, Sprite? baseSprite)
    {
        public Color BaseColor { get; set; } = baseColor;
        public Sprite? BaseSprite { get; set; } = baseSprite;
        public Color CustomColor { get; set; }
        public Sprite? CustomSprite { get; set; }
        public bool HasCustomColor { get; set; }
        public bool HasCustomSprite { get; set; }
    }
}
