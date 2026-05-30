using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.DirectPlayForce.Filters;

/// <summary>
/// Clears the SmartFallback confirmed-fallback state when a playback session ends,
/// so the next play attempt starts fresh.
/// </summary>
public class PlaybackStopHandler : IEventConsumer<PlaybackStopEventArgs>
{
    /// <inheritdoc />
    public Task OnEvent(PlaybackStopEventArgs eventArgs)
    {
        var deviceId = eventArgs.DeviceId;
        var itemId = eventArgs.Item?.Id.ToString("N");
        if (deviceId is not null && itemId is not null)
            DirectPlayForceFilter.ClearFallback($"{deviceId}:{itemId}");
        return Task.CompletedTask;
    }
}
