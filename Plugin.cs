using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.DirectPlayForce.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DirectPlayForce;

/// <summary>
/// DirectPlayForce — Jellyfin plugin that forces direct play for configured clients,
/// blocking all server-side transcoding. Delivers media files byte-for-byte as stored.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <inheritdoc />
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "DirectPlayForce";

    // Must match the GUID in meta.json, manifest.json, and the JS PLUGIN_ID constant.
    /// <inheritdoc />
    public override Guid Id => Guid.Parse("3f7a9c2e-b1d4-4e8a-a5f6-2c0d1e9b3f7a");

    /// <summary>Active plugin instance, used by the filter to read configuration.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.ConfigurationPage.html",
                    GetType().Namespace)
            }
        };
    }
}
