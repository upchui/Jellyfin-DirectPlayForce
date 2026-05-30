using System;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, DateTime> _pendingRetry = new();
    private static readonly ConcurrentDictionary<string, byte> _confirmedFallback = new();

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

        // SmartFallback: two-phase fallback detection
        var itemId = ExtractItemId(request.Path);
        var retryKey = $"{deviceId}:{itemId}";
        var isFallbackRetry = false;

        if (matchingRule.SmartFallback && itemId is not null)
        {
            if (_confirmedFallback.ContainsKey(retryKey))
            {
                isFallbackRetry = true;
                _logger.LogInformation(
                    "DirectPlayForce: Client='{Client}' Device='{Device}' item={Item} — confirmed fallback, passing through to Jellyfin",
                    clientName, deviceName, itemId);
            }
            else if (_pendingRetry.TryGetValue(retryKey, out var forcedAt)
                     && DateTime.UtcNow - forcedAt < TimeSpan.FromSeconds(matchingRule.FallbackTimeoutSeconds))
            {
                isFallbackRetry = true;
                _pendingRetry.TryRemove(retryKey, out _);
                _confirmedFallback[retryKey] = 0;
                _logger.LogInformation(
                    "DirectPlayForce: Client='{Client}' Device='{Device}' item={Item} — retry detected, fallback confirmed",
                    clientName, deviceName, itemId);
            }
        }

        // Execute the action, then patch the response
        var executedContext = await next().ConfigureAwait(false);

        if (executedContext.Exception is not null && !executedContext.ExceptionHandled)
            return;

        if (isFallbackRetry)
            return;

        ForceDirectPlay(executedContext, matchingRule, clientName, deviceName);

        if (matchingRule.SmartFallback && itemId is not null)
            _pendingRetry[retryKey] = DateTime.UtcNow;
    }

    internal static void ClearFallback(string key)
    {
        _confirmedFallback.TryRemove(key, out _);
        _pendingRetry.TryRemove(key, out _);
    }

    // ── Patch PlaybackInfoResponse to force direct play ───────────────────

    private void ForceDirectPlay(
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext execCtx,
        DirectPlayRule rule,
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
            t.GetProperty("SupportsDirectPlay")?.SetValue(ms, true);
            t.GetProperty("SupportsDirectStream")?.SetValue(ms, true);
            t.GetProperty("TranscodingUrl")?.SetValue(ms, string.Empty);
            patched++;
        }

        _logger.LogInformation(
            "DirectPlayForce: Client='{Client}' Device='{Device}' — {Patched} source(s) forced to direct play",
            clientName, deviceName, patched);
    }

    private static string? ExtractItemId(PathString path)
    {
        var value = path.Value;
        if (value is null) return null;
        var end = value.LastIndexOf("/PlaybackInfo", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        var start = value.LastIndexOf('/', end - 1);
        return (start >= 0 && start < end) ? value[(start + 1)..end] : null;
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
