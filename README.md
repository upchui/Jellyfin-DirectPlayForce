# DirectPlayForce

![DirectPlayForce Logo](img/image.png)

[![Build Check](https://github.com/upchui/Jellyfin-DirectPlayForce/actions/workflows/build.yml/badge.svg)](https://github.com/upchui/Jellyfin-DirectPlayForce/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/upchui/Jellyfin-DirectPlayForce)](https://github.com/upchui/Jellyfin-DirectPlayForce/releases/latest)
[![Jellyfin 10.9+](https://img.shields.io/badge/Jellyfin-10.9%2B-00a4dc)](https://jellyfin.org)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A [Jellyfin](https://jellyfin.org) server plugin that forces **direct play** for configured clients. The server delivers the original media file byte-for-byte — no FFmpeg, no re-encoding, zero quality loss.

---

## The Problem

The Jellyfin Android TV app on NVIDIA Shield (and other Android TV devices) reports **EAC3 / Dolby Digital Plus** as supported in its device profile. Jellyfin trusts this and delivers EAC3 via HLS/TS remux. However, ExoPlayer on the Shield plays EAC3 incorrectly — extreme stuttering, audio freeze, roughly one frame per second.

No amount of server-side transcoding to another codec produces a consistently working result for 7.1 surround in the HLS/TS container. The only reliable solution: **force the server to deliver the original MKV file via direct play**, letting ExoPlayer handle all codecs natively.

---

## Features

- ✅ Hard-forces direct play for matched clients — all transcoding blocked
- ✅ Per-rule client/device/device-ID filtering
- ✅ **Smart Fallback** (on by default): first attempt forces direct play; if the player immediately retries (incompatible audio codec etc.), the second attempt lets Jellyfin decide naturally (transcode/remux)
- ✅ No FFmpeg, no re-encoding, no quality loss
- ✅ All channel configurations preserved (5.1, 7.1, Atmos)
- ✅ Other clients are completely unaffected
- ✅ Configuration via Jellyfin Dashboard — no server restart needed

---

## Requirements

- Jellyfin **10.9.0** or later
- .NET 8 runtime (provided by Jellyfin)

---

## Installation

### Option 1: Jellyfin Plugin Repository (recommended)

1. Open **Jellyfin Dashboard** → **Plugins** → **Repositories**
2. Click **+** and add:
   ```
   https://raw.githubusercontent.com/upchui/Jellyfin-DirectPlayForce/main/manifest.json
   ```
3. Go to **Catalog** and search for **DirectPlayForce**
4. Install and restart Jellyfin
5. Configure under **Dashboard → Plugins → DirectPlayForce**

### Option 2: Manual Installation

1. Download `Jellyfin.Plugin.DirectPlayForce.zip` from the [latest release](https://github.com/upchui/Jellyfin-DirectPlayForce/releases/latest)
2. Extract the contents into your Jellyfin plugins folder:
   ```
   /config/plugins/DirectPlayForce_<version>/
   ├── Jellyfin.Plugin.DirectPlayForce.dll
   └── meta.json
   ```
   Common plugin paths:
   | Environment | Path |
   |-------------|------|
   | Docker (jellyfin/jellyfin) | `/config/plugins/` |
   | Linux (systemd) | `/var/lib/jellyfin/plugins/` |
   | Windows | `%APPDATA%\Jellyfin\plugins\` |
3. Restart Jellyfin
4. Configure under **Dashboard → Plugins → DirectPlayForce**

---

## Configuration

### Add a Direct Play Rule

1. Open **Dashboard → Plugins → DirectPlayForce → Settings**
2. Click **+ Add rule**
3. Fill in the filters:

| Field | Description | Example |
|-------|-------------|---------|
| **Client Filter** | Substring of the client app name | `Android TV` |
| **Device Filter** | Substring of the device name | `Living Room` |
| **Device ID** | Exact device ID (optional, for single-device targeting) | *(leave empty)* |

4. Click **Save** — takes effect on the next playback session

### Smart Fallback

**Smart Fallback** (🔄) is **enabled by default** on every new rule. Disable it only if you want a hard force with no fallback at all.

With Smart Fallback enabled, the plugin handles files whose codecs the client cannot play natively (e.g. DTS audio):

| Attempt | Behavior |
|---------|----------|
| **1st request** | Direct play forced as usual |
| **Retry within 3 s** (player reported immediate failure) | Jellyfin's natural decision passes through — transcoding/remux proceeds |

This means:
- Files the client **can** play natively → forced to direct play on the first attempt ✓
- Files the client **cannot** play (e.g. DTS audio) → first attempt fails instantly → retry → Jellyfin transcodes (e.g. DTS → AAC) ✓

### Finding Your Client and Device Names

Check the Jellyfin log after starting playback:
```
DirectPlayForce: PlaybackInfo from Client='Jellyfin Android TV' Device='Living Room' — checking rules
```

Or look in **Dashboard → Active Streams** while a stream is running.

### Example: Fix EAC3 Stuttering on NVIDIA Shield

| Field | Value |
|-------|-------|
| Client Filter | `Android TV` |
| Device Filter | *(leave empty)* |
| Smart Fallback | Off *(disable — EAC3 works natively on Shield)* |

With this rule the Shield always receives the original file. EAC3, TrueHD, DTS-HD MA — all audio formats play natively through ExoPlayer.

### Example: Android TV with Mixed Codec Support

For a device that can play most codecs natively but not DTS:

| Field | Value |
|-------|-------|
| Client Filter | `Android TV` |
| Device Filter | `Bedroom` |
| Smart Fallback | On *(default)* |

Result: DD+/EAC3/TrueHD files → direct play. DTS files → direct play attempt fails immediately → Jellyfin transcodes DTS → AAC.

---

## How It Works

The plugin registers a global ASP.NET Core action filter that intercepts `POST /Items/{id}/PlaybackInfo` responses **after** Jellyfin processes them. When a matching rule is found, it patches every MediaSource in the response:

```
MediaSource.SupportsDirectPlay  = true
MediaSource.SupportsDirectStream = true
MediaSource.TranscodingUrl       = ""   ← no transcoding path offered
```

The client has no transcoding URL to fall back to and must use direct play.

**Smart Fallback** adds a second layer: after forcing direct play, the plugin records the device + item combination. If the same device requests PlaybackInfo for the same item again within 3 seconds (indicating the player rejected direct play immediately), the patch is skipped and Jellyfin's original response — including any transcoding URL — is returned unchanged.

---

## Build from Source

All compilation happens inside Docker — no local .NET SDK required.

```bash
git clone https://github.com/upchui/Jellyfin-DirectPlayForce.git
cd Jellyfin-DirectPlayForce
chmod +x build.sh
./build.sh
# Output: dist/Jellyfin.Plugin.DirectPlayForce.zip
```

**Requirements:** Docker with BuildKit (default since Docker 23).

---

## License

[MIT](LICENSE)
