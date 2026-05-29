#!/usr/bin/env bash
# DirectPlayForce — Plugin build script
# Builds everything inside a Docker container; no local .NET SDK required.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="${SCRIPT_DIR}/dist"

mkdir -p "${OUTPUT_DIR}"

echo "═══════════════════════════════════════════════════════"
echo "  DirectPlayForce — Jellyfin Plugin Build"
echo "═══════════════════════════════════════════════════════"
echo ""

DOCKER_BUILDKIT=1 docker build \
    --output "type=local,dest=${OUTPUT_DIR}" \
    --progress=plain \
    "${SCRIPT_DIR}"

echo ""
echo "─────────────────────────────────────────────────────"
echo "  Build successful!"
echo "  Plugin: ${OUTPUT_DIR}/Jellyfin.Plugin.DirectPlayForce.zip"
echo "─────────────────────────────────────────────────────"
echo ""
echo "  Installation:"
echo "  1. Unzip the file"
echo "  2. Copy Jellyfin.Plugin.DirectPlayForce.dll + meta.json"
echo "     to /config/plugins/DirectPlayForce_2.0.0.0/"
echo "  3. Restart Jellyfin"
echo "  4. Dashboard → Plugins → DirectPlayForce → Settings"
echo "─────────────────────────────────────────────────────"
