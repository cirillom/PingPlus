using RoR2.UI;
using UnityEngine;

namespace PingPlus;

internal sealed class PinnedPing
{
    private static readonly string[] Names =
    [
        "ALPHA",
        "GAMMA",
        "BETA",
        "TETA",
        "LAMBDA"
    ];

    public PinnedPing(GameObject owner, GameObject? target, PingIndicator indicator, int slot)
    {
        Owner = owner;
        Target = target;
        Indicator = indicator;
        Slot = slot;
    }

    public GameObject Owner { get; }
    public GameObject? Target { get; }
    public PingIndicator Indicator { get; }
    public int Slot { get; }

    public static string GetDisplayName(int slot)
    {
        return slot >= 0 && slot < Names.Length ? Names[slot] : $"PING {slot + 1}";
    }
}
