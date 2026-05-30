using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DirectPlayForce.Configuration;

/// <summary>
/// Plugin configuration — persisted as XML in the Jellyfin plugin data directory.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Master switch: disable without losing configuration.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Direct play rules. When a rule matches an incoming client request,
    /// the plugin forces direct play and blocks all transcoding for that session.
    /// </summary>
    public DirectPlayRule[] DirectPlayRules { get; set; } = Array.Empty<DirectPlayRule>();
}

/// <summary>
/// Forces direct play for a specific client or device.
/// </summary>
public class DirectPlayRule
{
    /// <summary>Unique rule identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Enable or pause this rule without deleting it.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Substring match against the client name from the Authorization header
    /// (e.g. "Android TV" matches "Jellyfin Android TV"). Empty = all clients.
    /// </summary>
    public string ClientFilter { get; set; } = string.Empty;

    /// <summary>
    /// Substring match against the device name (e.g. "Living Room").
    /// Empty = all devices.
    /// </summary>
    public string DeviceFilter { get; set; } = string.Empty;

    /// <summary>Exact DeviceId match. Empty = all device IDs.</summary>
    public string DeviceIdFilter { get; set; } = string.Empty;

    /// <summary>
    /// When enabled: forces direct play on the first attempt. If the player immediately
    /// retries within FallbackTimeoutSeconds, the second request is passed through
    /// to Jellyfin unchanged — allowing its natural transcoding/remux decision.
    /// </summary>
    public bool SmartFallback { get; set; } = true;

    /// <summary>
    /// Seconds to wait for a player retry before SmartFallback detection expires.
    /// </summary>
    public int FallbackTimeoutSeconds { get; set; } = 3;
}
