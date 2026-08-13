#!/usr/bin/env bash
#
# build_release.sh — Build the CampfireBugFix mod and assemble all mod
# artifacts into a single installable folder.
#
# Steps:
#   1. dotnet build        ->  .godot/mono/temp/bin/Debug/CampfireBugFix.dll
#   2. Godot 4.5.x headless ->  build/CampfireBugFix.pck
#   3. Assemble release     ->  build/CampfireBugFix/
#          ├── CampfireBugFix.json
#          ├── CampfireBugFix.pck
#          └── CampfireBugFix.dll
#
# The PCK MUST be packed with Godot 4.5.x (the version the game ships).
# Newer Godot versions (4.6/4.7+) write PCK format v4 which Slay the Spire 2
# cannot load. On Linux the STS2 community "MegaDot 4.5.1" build is a good
# fit; this script auto-detects it.
#
# Usage:
#   ./tools/build_release.sh            # build + assemble release folder
#   ./tools/build_release.sh --zip      # also zip the release folder
#   GODOT=/path/to/godot-4.5 ./tools/build_release.sh   # explicit 4.5.x binary
#
set -euo pipefail

# ---------------------------------------------------------------- options --
DO_ZIP=0
for arg in "$@"; do
    case "$arg" in
        --zip) DO_ZIP=1 ;;
        --help|-h)
            grep -E '^#   |^# \(' "$0" | sed 's/^#   //; s/^# //'
            exit 0
            ;;
        *)
            echo "Unknown option: $arg (try --help)" >&2
            exit 2
            ;;
    esac
done

# ------------------------------------------------------------- locations --
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

MOD_ID="CampfireBugFix"
DLL_SOURCE="$ROOT_DIR/.godot/mono/temp/bin/Debug/${MOD_ID}.dll"
PCK_OUTPUT="$ROOT_DIR/build/${MOD_ID}.pck"
RELEASE_DIR="$ROOT_DIR/build/${MOD_ID}"
MANIFEST="$ROOT_DIR/${MOD_ID}.json"

log()  { printf '\033[1;34m[build]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[build]\033[0m WARNING: %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[build]\033[0m ERROR: %s\n' "$*" >&2; exit 1; }

# ------------------------------------------------------------- toolchain --
command -v dotnet >/dev/null 2>&1 || die "dotnet not found in PATH (need .NET 9 SDK)"
log "dotnet: $(dotnet --version)"

# Locate a Godot 4.5.x binary. Priority: $GODOT env var > megadot > godot.
GODOT_BIN="${GODOT:-}"
if [[ -z "$GODOT_BIN" ]]; then
    for candidate in megadot godot; do
        if command -v "$candidate" >/dev/null 2>&1; then
            GODOT_BIN="$(command -v "$candidate")"
            break
        fi
    done
fi
[[ -n "$GODOT_BIN" ]] || die "No Godot binary found. Install megadot (4.5.1) or set GODOT=/path/to/godot-4.5.x"

GODOT_VERSION="$("$GODOT_BIN" --version 2>/dev/null | head -n1 || true)"
log "godot: $GODOT_BIN ($GODOT_VERSION)"

# The PCK must be packed with 4.5.x — refuse anything else.
case "$GODOT_VERSION" in
    4.5.*) : ;;
    *)
        die "Godot 4.5.x is required to pack the PCK (found '$GODOT_VERSION'). \
Newer versions write PCK format v4 that the game cannot load. \
Install megadot 4.5.1 or set GODOT=/path/to/godot-4.5.x"
        ;;
esac

# ------------------------------------------------------------ prerequisites --
[[ -f "$ROOT_DIR/sts2.dll" ]] || die "sts2.dll not found in project root — copy it from your Slay the Spire 2 install (see README)"
[[ -f "$MANIFEST" ]]          || die "manifest not found: $MANIFEST"

# ------------------------------------------------------------------ build --
log "Building C# project (dotnet build)..."
dotnet build -nologo

[[ -f "$DLL_SOURCE" ]] || die "build failed: expected DLL at $DLL_SOURCE"

# The PCK builder only packs mod_image.png if the project has been imported
# once (it embeds the .import/.ctex chain). Auto-import headlessly when the
# ctex is missing, so a missing thumbnail isn't discovered only in-game.
if ! find "$ROOT_DIR/.godot/imported" -maxdepth 1 -name "mod_image*.ctex" 2>/dev/null | grep -q .; then
    log "mod_image.png not imported yet — running headless import..."
    "$GODOT_BIN" --headless --import >/dev/null 2>&1 || true
    if ! find "$ROOT_DIR/.godot/imported" -maxdepth 1 -name "mod_image*.ctex" 2>/dev/null | grep -q .; then
        warn "No imported ctex for $MOD_ID/mod_image.png found after import —"
        warn "the mod image will NOT be packed into the PCK. Open this project once"
        warn "in the Godot 4.5 editor (it imports the image), then re-run this script."
    fi
fi

log "Packing PCK with Godot 4.5.x (tools/build_pck.gd)..."
"$GODOT_BIN" --headless --script tools/build_pck.gd

[[ -f "$PCK_OUTPUT" ]] || die "pck build failed: expected $PCK_OUTPUT"

# --------------------------------------------------------------- assemble --
log "Assembling release folder: $RELEASE_DIR"
rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

cp "$MANIFEST"    "$RELEASE_DIR/"
cp "$PCK_OUTPUT"  "$RELEASE_DIR/"
cp "$DLL_SOURCE"  "$RELEASE_DIR/"

log "Release contents:"
ls -la "$RELEASE_DIR"

# -------------------------------------------------------------- optional zip --
if [[ "$DO_ZIP" -eq 1 ]]; then
    ZIP_PATH="$ROOT_DIR/build/${MOD_ID}.zip"
    log "Creating zip: $ZIP_PATH"
    rm -f "$ZIP_PATH"
    (cd "$ROOT_DIR/build" && zip -r -q "${MOD_ID}.zip" "${MOD_ID}")
    ls -la "$ZIP_PATH"
fi

log "Done. Copy the '$MOD_ID' folder into the game's Mods directory."
