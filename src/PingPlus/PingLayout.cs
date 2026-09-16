using RoR2.UI;
using TMPro;
using UnityEngine;

namespace PingPlus;

internal static class PingLayout
{
    private const float ScreenPadding = 8f;

    public static void Update(PingIndicator indicator)
    {
        if (!indicator.pingText)
            return;

        indicator.pingText.overflowMode = TextOverflowModes.Overflow;
    }

    public static void ClampToViewport(PingIndicator indicator, Camera camera)
    {
        if (!indicator.positionIndicator || !camera)
            return;

        if (indicator.pingText && indicator.pingText.enabled)
            indicator.pingText.ForceMeshUpdate();

        var root = indicator.positionIndicator.transform;
        var rootScreenPosition = camera.WorldToScreenPoint(root.position);

        if (rootScreenPosition.z <= 0f || !TryGetScreenBounds(indicator, camera, out var min, out var max))
            return;

        var viewport = camera.pixelRect;
        var safeArea = Screen.safeArea;
        var left = Mathf.Max(viewport.xMin, safeArea.xMin) + ScreenPadding;
        var right = Mathf.Min(viewport.xMax, safeArea.xMax) - ScreenPadding;
        var bottom = Mathf.Max(viewport.yMin, safeArea.yMin) + ScreenPadding;
        var top = Mathf.Min(viewport.yMax, safeArea.yMax) - ScreenPadding;
        var correction = Vector2.zero;

        if (min.x < left)
            correction.x += left - min.x;
        else if (max.x > right)
            correction.x -= max.x - right;

        if (min.y < bottom)
            correction.y += bottom - min.y;
        else if (max.y > top)
            correction.y -= max.y - top;

        if (correction == Vector2.zero)
            return;

        rootScreenPosition.x += correction.x;
        rootScreenPosition.y += correction.y;
        root.position = camera.ScreenToWorldPoint(rootScreenPosition);
    }

    private static bool TryGetScreenBounds(
        PingIndicator indicator,
        Camera camera,
        out Vector2 min,
        out Vector2 max)
    {
        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var foundRenderer = false;

        foreach (var renderer in indicator.positionIndicator.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            var bounds = renderer.bounds;

            for (var corner = 0; corner < 8; corner++)
            {
                var worldPosition = new Vector3(
                    (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                var screenPosition = camera.WorldToScreenPoint(worldPosition);

                if (screenPosition.z <= 0f)
                    continue;

                min = Vector2.Min(min, screenPosition);
                max = Vector2.Max(max, screenPosition);
                foundRenderer = true;
            }
        }

        return foundRenderer;
    }
}
