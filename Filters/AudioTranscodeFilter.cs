using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.DirectPlayForce.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DirectPlayForce.Filters;

/// <summary>
/// Global ASP.NET Core action filter that enforces direct play for configured clients.
/// When a matching rule is found, the filter patches the PlaybackInfo response to set
/// SupportsDirectPlay and SupportsDirectStream to true and clears the TranscodingUrl —
/// leaving the client no path to a transcoded stream.
/// </summary>
public class DirectPlayForceFilter : IAsyncActionFilter
{
    private readonly ILogger<DirectPlayForceFilter> _logger;

    /// <summary>Initializes a new instance of <see cref="DirectPlayForceFilter"/>.</summary>
    public DirectPlayForceFilter(ILogger<DirectPlayForceFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // Only intercept POST /Items/{id}/PlaybackInfo
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method) ||
            !(request.Path.Value?.EndsWith("/PlaybackInfo", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Master switch
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.IsEnabled || config.DirectPlayRules.Length == 0)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Parse client identity from the Authorization header
        var authHeader = request.Headers["X-Emby-Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader))
            authHeader = request.Headers["Authorization"].ToString();

        ParseAuthHeader(authHeader, out var clientName, out var deviceName, out var deviceId);

        _logger.LogInformation(
            "DirectPlayForce: PlaybackInfo from Client='{Client}' Device='{Device}' — checking rules",
            clientName, deviceName);

        // Find the first matching rule
        DirectPlayRule? matchingRule = null;
        foreach (var rule in config.DirectPlayRules)
        {
            if (!rule.IsEnabled) continue;
            if (MatchesClientFields(rule.ClientFilter, rule.DeviceFilter, rule.DeviceIdFilter,
                                    clientName, deviceName, deviceId))
            {
                matchingRule = rule;
                break;
            }
        }

        if (matchingRule is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Execute the action, then patch the response
        var executedContext = await next().ConfigureAwait(false);

        if (executedContext.Exception is not null && !executedContext.ExceptionHandled)
            return;

        ForceDirectPlay(executedContext, clientName, deviceName);
    }

    // ── Patch PlaybackInfoResponse to force direct play ───────────────────

    private void ForceDirectPlay(
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext execCtx,
        string clientName,
        string deviceName)
    {
        if (execCtx.Result is not ObjectResult { Value: { } responseVal })
            return;

        var sourcesProperty = responseVal.GetType().GetProperty("MediaSources");
        if (sourcesProperty?.GetValue(responseVal) is not IEnumerable<object> mediaSources)
            return;

        var patched = 0;
        foreach (var ms in mediaSources)
        {
            var t = ms.GetType();
            // Allow direct play and direct stream
            t.GetProperty("SupportsDirectPlay")?.SetValue(ms, true);
            t.GetProperty("SupportsDirectStream")?.SetValue(ms, true);
            // Clear the transcoding URL so the client has no transcoded stream to fall back to
            t.GetProperty("TranscodingUrl")?.SetValue(ms, string.Empty);
            patched++;
        }

        _logger.LogInformation(
            "DirectPlayForce: Direct play enforced for Client='{Client}' Device='{Device}' ({Count} media source(s))",
            clientName, deviceName, patched);
    }

    // ── Client filter matching ────────────────────────────────────────────

    private static bool MatchesClientFields(
        string clientFilter,
        string deviceFilter,
        string deviceIdFilter,
        string clientName,
        string deviceName,
        string deviceId)
    {
        if (!string.IsNullOrEmpty(clientFilter)
            && !clientName.Contains(clientFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(deviceFilter)
            && !deviceName.Contains(deviceFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(deviceIdFilter)
            && !string.Equals(deviceId, deviceIdFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    // ── Authorization header parser ───────────────────────────────────────
    // Format: MediaBrowser Client="...", Device="...", DeviceId="...", ...

    private static void ParseAuthHeader(
        string header,
        out string client,
        out string device,
        out string deviceId)
    {
        client   = string.Empty;
        device   = string.Empty;
        deviceId = string.Empty;

        var content = header.StartsWith("MediaBrowser ", StringComparison.OrdinalIgnoreCase)
            ? header["MediaBrowser ".Length..]
            : header;

        foreach (var pair in SplitAuthPairs(content))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var key = pair[..eq].Trim();
            // Replace '+' with space: Jellyfin Android TV sends URL-encoded client names
            var val = pair[(eq + 1)..].Trim().Trim('"').Replace('+', ' ');

            if (key.Equals("Client", StringComparison.OrdinalIgnoreCase))   client   = val;
            else if (key.Equals("Device", StringComparison.OrdinalIgnoreCase))   device   = val;
            else if (key.Equals("DeviceId", StringComparison.OrdinalIgnoreCase)) deviceId = val;
        }
    }

    private static IEnumerable<string> SplitAuthPairs(string content)
    {
        var current = new StringBuilder();
        var inQuote = false;
        foreach (var ch in content)
        {
            if (ch == '"') { inQuote = !inQuote; current.Append(ch); }
            else if (ch == ',' && !inQuote) { yield return current.ToString().Trim(); current.Clear(); }
            else { current.Append(ch); }
        }
        if (current.Length > 0) yield return current.ToString().Trim();
    }
}
