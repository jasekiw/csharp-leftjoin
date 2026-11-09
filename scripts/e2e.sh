#!/usr/bin/env bash
# End-to-end check of the patched compiler:
#   1. build + pack Roslyn (skip with SKIP_COMPILER_BUILD=1)
#   2. build the e2e tests with the patched compiler and prove that compiler was the one used
#   3. run the tests
#   4. build the same tests with the stock SDK compiler and require it to fail on the new syntax
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
e2e="$repo_root/e2e"
project="$e2e/LeftJoin.E2E.Tests/LeftJoin.E2E.Tests.csproj"
logs="$e2e/LeftJoin.E2E.Tests/bin/e2e-logs"

if [[ "${SKIP_COMPILER_BUILD:-0}" == 1 ]]; then
  SKIP_ROSLYN_BUILD=1 "$repo_root/scripts/build-compiler.sh"
else
  "$repo_root/scripts/build-compiler.sh"
fi

version="$(sed -n 's:.*<PatchedCompilerVersion>\(.*\)</PatchedCompilerVersion>.*:\1:p' "$e2e/PatchedCompiler.props")"

# Locally built packages reuse one version number, so a shared NuGet cache would keep serving
# the first build. Use a private cache and drop the compiler packages from it on every run.
export NUGET_PACKAGES="$e2e/.nuget-packages"
rm -rf "$NUGET_PACKAGES"/microsoft.net.compilers.toolset \
       "$NUGET_PACKAGES"/microsoft.codeanalysis.common \
       "$NUGET_PACKAGES"/microsoft.codeanalysis.csharp
dotnet build-server shutdown > /dev/null 2>&1 || true
mkdir -p "$logs"

cd "$e2e"

echo "==> Building with patched compiler $version"
dotnet build "$project" -c Release --no-incremental -v:n > "$logs/patched-build.log" 2>&1 || {
  grep -E 'error|warning CS' "$logs/patched-build.log" | sort -u >&2
  echo "Patched build failed; full log: $logs/patched-build.log" >&2
  exit 1
}
if ! grep -q "compiling with patched csc $version .*microsoft.net.compilers.toolset/$version/tasks/netcore/bincore/csc.dll" "$logs/patched-build.log" ||
   ! grep -q "patched-csc/csc " "$logs/patched-build.log"; then
  echo "Build did not run csc.dll from Microsoft.Net.Compilers.Toolset $version; see $logs/patched-build.log" >&2
  exit 1
fi
echo "    csc.dll came from Microsoft.Net.Compilers.Toolset $version"

echo "==> Running tests"
dotnet test "$project" -c Release --no-build

echo "==> Building with the stock SDK compiler (must fail on leftjoin/rightjoin)"
stock="$(mktemp -d)"
trap 'rm -rf "$stock"' EXIT
if dotnet build "$project" -c Release -p:UsePatchedCompiler=false \
     -p:BaseIntermediateOutputPath="$stock/obj/" -p:OutputPath="$stock/bin/" > "$logs/stock-build.log" 2>&1; then
  echo "Stock compiler built the e2e tests; they do not exercise the patch" >&2
  exit 1
fi
if ! grep -qE 'error CS(0742|1003)' "$logs/stock-build.log"; then
  echo "Stock build failed for an unexpected reason; see $logs/stock-build.log" >&2
  exit 1
fi
echo "    Stock compiler rejects the new syntax as expected"

echo "==> E2E passed"
