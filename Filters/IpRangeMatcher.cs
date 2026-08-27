using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DirectPlayForce.Filters;

/// <summary>
/// Parses and matches the configured excluded IP ranges. Supported entry formats:
/// CIDR ("192.168.1.0/24"), single IP ("192.168.1.5") and
/// start-end ranges ("192.168.1.10-192.168.1.50"), IPv4 and IPv6.
/// </summary>
internal static class IpRangeMatcher
{
    // Parsed ranges are memoized per config array instance: updatePluginConfiguration
    // deserializes a fresh PluginConfiguration, so a changed reference means new config.
    private static readonly object _cacheLock = new();
    private static string[]? _cachedSource;
    private static List<ParsedRange>? _cachedRanges;

    /// <summary>
    /// Returns true when <paramref name="remoteIp"/> falls inside any of the configured
    /// <paramref name="excludedRanges"/>. Invalid entries are skipped (logged once per
    /// config version) and never cause a match.
    /// </summary>
    internal static bool IsExcluded(IPAddress? remoteIp, string[] excludedRanges, ILogger logger)
    {
        if (remoteIp is null || excludedRanges.Length == 0)
            return false;

        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        List<ParsedRange> ranges;
        lock (_cacheLock)
        {
            if (!ReferenceEquals(excludedRanges, _cachedSource) || _cachedRanges is null)
            {
                _cachedRanges = Parse(excludedRanges, logger);
                _cachedSource = excludedRanges;
            }

            ranges = _cachedRanges;
        }

        foreach (var range in ranges)
        {
            if (range.Contains(remoteIp))
                return true;
        }

        return false;
    }

    private static List<ParsedRange> Parse(string[] entries, ILogger logger)
    {
        var result = new List<ParsedRange>(entries.Length);
        foreach (var raw in entries)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrEmpty(entry))
                continue;

            if (TryParseEntry(entry, out var parsed))
                result.Add(parsed);
            else
                logger.LogWarning("DirectPlayForce: ignoring invalid excluded IP range entry '{Entry}'", entry);
        }

        return result;
    }

    private static bool TryParseEntry(string entry, out ParsedRange parsed)
    {
        parsed = default;

        // CIDR: "192.168.1.0/24"
        var slash = entry.IndexOf('/');
        if (slash >= 0)
        {
            if (!IPAddress.TryParse(entry[..slash], out var baseAddress)
                || !int.TryParse(entry[(slash + 1)..], out var prefix))
                return false;

            baseAddress = Normalize(baseAddress);
            var bytes = baseAddress.GetAddressBytes();
            if (prefix < 0 || prefix > bytes.Length * 8)
                return false;

            // Mask host bits: .NET 8 IPNetwork rejects base addresses with host bits set.
            for (var i = 0; i < bytes.Length; i++)
            {
                var bitsLeft = prefix - i * 8;
                bytes[i] &= bitsLeft >= 8 ? (byte)0xFF
                          : bitsLeft <= 0 ? (byte)0x00
                          : (byte)(0xFF << (8 - bitsLeft));
            }

            parsed = ParsedRange.FromNetwork(new IPNetwork(new IPAddress(bytes), prefix));
            return true;
        }

        // Start-end range: "192.168.1.10-192.168.1.50"
        var dash = entry.IndexOf('-');
        if (dash >= 0
            && IPAddress.TryParse(entry[..dash].Trim(), out var start)
            && IPAddress.TryParse(entry[(dash + 1)..].Trim(), out var end))
        {
            start = Normalize(start);
            end = Normalize(end);
            if (start.AddressFamily != end.AddressFamily)
                return false;

            if (CompareBytes(start.GetAddressBytes(), end.GetAddressBytes()) > 0)
                (start, end) = (end, start);

            parsed = ParsedRange.FromBounds(start, end);
            return true;
        }

        // Single IP
        if (IPAddress.TryParse(entry, out var single))
        {
            single = Normalize(single);
            parsed = ParsedRange.FromBounds(single, single);
            return true;
        }

        return false;
    }

    private static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static int CompareBytes(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return a[i] < b[i] ? -1 : 1;
        }

        return 0;
    }

    private readonly struct ParsedRange
    {
        private readonly IPNetwork? _network;
        private readonly IPAddress? _start;
        private readonly IPAddress? _end;

        private ParsedRange(IPNetwork? network, IPAddress? start, IPAddress? end)
        {
            _network = network;
            _start = start;
            _end = end;
        }

        internal static ParsedRange FromNetwork(IPNetwork network) => new(network, null, null);

        internal static ParsedRange FromBounds(IPAddress start, IPAddress end) => new(null, start, end);

        internal bool Contains(IPAddress ip)
        {
            if (_network is { } network)
                return network.Contains(ip);

            if (_start is null || _end is null || ip.AddressFamily != _start.AddressFamily)
                return false;

            var bytes = ip.GetAddressBytes();
            return CompareBytes(_start.GetAddressBytes(), bytes) <= 0
                && CompareBytes(bytes, _end.GetAddressBytes()) <= 0;
        }
    }
}
