using RoR2;
using RoR2.UI;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PingPlus;

internal static class PingDistance
{
    private const char SuffixMarker = '\u200B';
    private static readonly ConditionalWeakTable<PingIndicator, State> States = new();

    public static void Update(PingIndicator indicator, bool enabled)
    {
        if (!indicator.pingText)
            return;

        var state = States.GetValue(indicator, static _ => new State());
        var currentText = indicator.pingText.text;

        if (currentText != state.RenderedText)
        {
            state.BaseText = StripDistance(currentText);
            state.LastMeters = int.MinValue;
        }

        var localBody = LocalUserManager.GetFirstLocalUser()?.cachedBody;

        if (!enabled || localBody == null)
        {
            if (currentText != state.BaseText)
                indicator.pingText.text = state.BaseText;

            state.RenderedText = state.BaseText;
            state.LastMeters = int.MinValue;
            return;
        }

        var meters = Mathf.RoundToInt(Vector3.Distance(localBody.footPosition, indicator.transform.position));

        if (meters == state.LastMeters && currentText == state.RenderedText)
            return;

        state.RenderedText = $"{state.BaseText}{SuffixMarker}<size=70%> ({meters}m)</size>";
        state.LastMeters = meters;
        indicator.pingText.text = state.RenderedText;
    }

    private static string StripDistance(string text)
    {
        var markerIndex = text.IndexOf(SuffixMarker);
        return markerIndex >= 0 ? text[..markerIndex] : text;
    }

    private sealed class State
    {
        public string BaseText { get; set; } = string.Empty;
        public string RenderedText { get; set; } = string.Empty;
        public int LastMeters { get; set; } = int.MinValue;
    }
}
