#!/usr/bin/env bash
# Usage: bash .github/scripts/cloud-test-env.sh <command> [args...]
# The cloud image's 52-orbit-os.conf prefers OpenAI Sans (WOFF2). SkiaSharp 3.119.4
# cannot load it: SKTypeface.Default has zero glyphs, so unconfigured headless
# builders cannot even build text geometry. Exclude those unsupported files and
# retain the distro's normal fallback order. The shipped builder bundles Inter.
# ponytail: this font directory is specific to this cloud image; no font install.
set -euo pipefail
if [ "$#" -eq 0 ]; then
  echo "usage: $0 <command> [args...]" >&2
  exit 2
fi
font_env=$(mktemp -d /tmp/ccp-fontconfig.XXXXXX)
trap 'rm -rf "$font_env"' EXIT
export XDG_CACHE_HOME="$font_env/cache"
mkdir -p "$XDG_CACHE_HOME"
export FONTCONFIG_FILE="$font_env/fonts.conf"
cat > "$FONTCONFIG_FILE" <<'XML'
<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <include>/etc/fonts/fonts.conf</include>
  <selectfont><rejectfont>
    <glob>/usr/share/fonts/openai-sans/*.woff2</glob>
  </rejectfont></selectfont>
</fontconfig>
XML
"$@"
