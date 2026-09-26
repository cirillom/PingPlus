using RoR2.UI;
using UnityEngine.Networking;

namespace PingPlus;

internal sealed class PinnedPing
{
    private static readonly string[] Names =
    [
        "ALPHA",
        "BETA",
        "GAMMA",
        "DELTA",
        "EPSILON",
        "ZETA",
        "ETA",
        "THETA",
        "IOTA",
        "KAPPA",
        "LAMBDA",
        "MU",
        "NU",
        "XI",
        "OMICRON",
        "PI",
        "RHO",
        "SIGMA",
        "TAU",
        "UPSILON"
    ];

    public PinnedPing(
        NetworkInstanceId ownerId,
        NetworkInstanceId targetId,
        PingIndicator indicator,
        int labelIndex)
    {
        OwnerId = ownerId;
        TargetId = targetId;
        Indicator = indicator;
        LabelIndex = labelIndex;
    }

    public NetworkInstanceId OwnerId { get; }
    public NetworkInstanceId TargetId { get; }
    public PingIndicator Indicator { get; }
    public int LabelIndex { get; }

    public static string GetDisplayName(int labelIndex)
    {
        return labelIndex >= 0 && labelIndex < Names.Length ? Names[labelIndex] : $"PING {labelIndex + 1}";
    }
}
