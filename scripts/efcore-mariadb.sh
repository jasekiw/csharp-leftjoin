#!/usr/bin/env bash
# Runs samples/EfCoreMariaDb: leftjoin/rightjoin queries through EF Core against MariaDB in Docker.
#   1. build + pack Roslyn (skip with SKIP_COMPILER_BUILD=1)
#   2. start MariaDB with docker compose (starting Docker Desktop if needed) and wait until healthy
#   3. build the sample with the patched compiler and run it
#
#   MARIADB_PORT=3307   host port for MariaDB
#   DOWN=1              stop and remove the MariaDB container afterwards
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sample="$repo_root/samples/EfCoreMariaDb"
compose=(docker compose -f "$sample/docker-compose.yml")
port="${MARIADB_PORT:-3307}"
export MARIADB_PORT="$port"

ensure_docker() {
  if docker info > /dev/null 2>&1; then
    return
  fi
  if systemctl --user cat docker-desktop.service > /dev/null 2>&1; then
    echo "==> Starting Docker Desktop"
    systemctl --user start docker-desktop
    for _ in $(seq 1 90); do
      if docker info > /dev/null 2>&1; then
        return
      fi
      sleep 2
    done
  fi
  echo "Docker is not running. Start Docker and rerun." >&2
  exit 1
}

if [[ "${SKIP_COMPILER_BUILD:-0}" == 1 ]]; then
  SKIP_ROSLYN_BUILD=1 "$repo_root/scripts/build-compiler.sh"
else
  "$repo_root/scripts/build-compiler.sh"
fi

ensure_docker
echo "==> Starting MariaDB on port $port"
"${compose[@]}" up -d --wait

if [[ "${DOWN:-0}" == 1 ]]; then
  trap '"${compose[@]}" down' EXIT
fi

# Locally built compiler packages reuse one version number; keep them out of the shared NuGet cache.
export NUGET_PACKAGES="$sample/.nuget-packages"
rm -rf "$NUGET_PACKAGES"/microsoft.net.compilers.toolset
dotnet build-server shutdown > /dev/null 2>&1 || true

echo "==> Building the sample with the patched compiler"
dotnet build "$sample/EfCoreMariaDb.csproj" -c Release -nologo -v:q -clp:NoSummary

echo "==> Running"
MARIADB_CONNECTION="Server=127.0.0.1;Port=$port;Database=university;User=demo;Password=demo;" \
  dotnet "$sample/bin/Release/net10.0/EfCoreMariaDb.dll"

if [[ "${DOWN:-0}" != 1 ]]; then
  echo "MariaDB is still running; stop it with: docker compose -f samples/EfCoreMariaDb/docker-compose.yml down"
fi
