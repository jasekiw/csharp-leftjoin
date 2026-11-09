#!/usr/bin/env bash
# Builds the fork's Roslyn language server and points Cursor's C# extension at it
# (through scripts/leftjoin-language-server) for this workspace, so the editor
# understands leftjoin/rightjoin. Reload the Cursor window afterwards.
#
#   SKIP_SERVER_BUILD=1   reuse the last language server build
#   SKIP_TEST=1           skip the scripted language server smoke test
#   UNINSTALL=1           remove dotnet.server.path from .vscode/settings.json
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
roslyn="$repo_root/roslyn"
adapter="$repo_root/scripts/leftjoin-language-server"
settings="$repo_root/.vscode/settings.json"

update_settings() {
  mkdir -p "$(dirname "$settings")"
  python3 - "$settings" "$1" <<'EOF'
import json, os, sys
path, server = sys.argv[1], sys.argv[2]
settings = {}
if os.path.exists(path):
    try:
        with open(path) as f:
            settings = json.load(f)
    except json.JSONDecodeError:
        sys.exit(f"{path} is not plain JSON (comments?); set \"dotnet.server.path\" by hand")
if server:
    settings["dotnet.server.path"] = server
else:
    settings.pop("dotnet.server.path", None)
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
    f.write("\n")
EOF
}

if [[ "${UNINSTALL:-0}" == 1 ]]; then
  update_settings ""
  echo "Removed dotnet.server.path from $settings; reload the Cursor window to use the bundled server."
  exit 0
fi

if [[ ! -x "$roslyn/restore.sh" ]]; then
  echo "Roslyn submodule missing; run: git submodule update --init" >&2
  exit 1
fi

if [[ "${SKIP_SERVER_BUILD:-0}" != 1 ]]; then
  [[ -x "$roslyn/.dotnet/dotnet" ]] || "$roslyn/restore.sh"
  "$roslyn/.dotnet/dotnet" build \
    "$roslyn/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/Microsoft.CodeAnalysis.LanguageServer.csproj" \
    -c Release -nologo -v:q
fi

if [[ "${SKIP_TEST:-0}" != 1 ]]; then
  python3 "$repo_root/scripts/test-leftjoin-lsp.py"
fi

update_settings "$adapter"
echo "Set dotnet.server.path in $settings"
echo "In Cursor run \"Developer: Reload Window\" to start the leftjoin-aware language server."
