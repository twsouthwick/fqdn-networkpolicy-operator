using System.Net;

namespace Swick.FqdnNetworkPolicyOperator;

internal sealed class IPNetworkComparer : IComparer<IPNetwork>
{
    public static readonly IPNetworkComparer Instance = new();

    public int Compare(IPNetwork x, IPNetwork y)
    {
        Span<byte> xBytes = stackalloc byte[16];
        Span<byte> yBytes = stackalloc byte[16];

        x.BaseAddress.TryWriteBytes(xBytes, out int xLen);
        y.BaseAddress.TryWriteBytes(yBytes, out int yLen);

        // IPv4 before IPv6
        int cmp = xLen.CompareTo(yLen);
        if (cmp != 0) return cmp;

        cmp = xBytes[..xLen].SequenceCompareTo(yBytes[..yLen]);
        if (cmp != 0) return cmp;

        return x.PrefixLength.CompareTo(y.PrefixLength);
    }
}
