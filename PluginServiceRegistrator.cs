using Jellyfin.Plugin.DirectPlayForce.Filters;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DirectPlayForce;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        serviceCollection.AddScoped<DirectPlayForceFilter>();

        serviceCollection.PostConfigure<MvcOptions>(options =>
        {
            options.Filters.Add<DirectPlayForceFilter>();
        });
    }
}
