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
- ✅ **Smart Fallback** (on by default): first attempt forces direct play; if the player immediately retries, Jellyfin's natural decision takes over — and stays active for the rest of that session
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

4. Optionally configure **Smart Fallback** (see below)
5. Click **Save** — takes effect on the next playback session

### Smart Fallback

**Smart Fallback** (🔄) is **enabled by default** on every new rule. Disable it only if you want a pure hard-force with zero fallback.

With Smart Fallback, the plugin uses two phases:

| Phase | Trigger | Behavior |
|-------|---------|----------|
| **1 — Force** | First PlaybackInfo request | Direct play forced as usual |
| **2 — Retry detected** | Player retries within the detection window | Fallback **confirmed**: Jellyfin's natural decision (transcode/remux) passes through for the rest of the session |
| **Session ends** | Player reports playback stopped | Fallback state cleared — next play starts fresh at Phase 1 |

**Detection window** (default 3 s) is configurable **per rule** via the *Fallback detection window (seconds)* field. When a codec failure causes the player to immediately request PlaybackInfo again, the retry arrives in well under a second — so 3 s reliably distinguishes a failure retry from an intentional restart.

Once fallback is confirmed, subsequent requests for the same item (e.g. changing audio tracks) **skip the direct play attempt entirely** — no more retry cycles.

This means:
- Files the client **can** play natively → forced to direct play, no fallback triggered ✓
- Files with an incompatible codec (e.g. DTS) → first attempt fails → retry → Jellyfin transcodes (e.g. DTS → AAC) for the rest of the session ✓

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

**Smart Fallback** adds a two-phase layer on top of the hard force:

1. **Pending** — after forcing direct play, the plugin records the device + item combination with a timestamp
2. **Confirmed** — if the player retries within the configured detection window (default 3 s), the fallback is confirmed and Jellyfin's original response passes through unchanged for the rest of the session
3. **Reset** — when the playback session ends (player reports stop), the confirmed fallback is cleared so the next play starts fresh

This eliminates retry loops when audio tracks are changed mid-session: once fallback is confirmed, every subsequent PlaybackInfo request for that item immediately uses Jellyfin's decision instead of attempting direct play again.

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
