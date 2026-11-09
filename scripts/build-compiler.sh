#!/usr/bin/env bash
# Builds and packs the Roslyn submodule, copies the compiler packages into local-feed/,
# and writes e2e/PatchedCompiler.props so the e2e project compiles with that compiler.
#
#   CONFIGURATION=Release      Roslyn build configuration
#   SKIP_ROSLYN_BUILD=1        reuse the packages from the last Roslyn build
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
roslyn="$repo_root/roslyn"
feed="$repo_root/local-feed"
configuration="${CONFIGURATION:-Release}"

if [[ ! -x "$roslyn/build.sh" ]]; then
  echo "Roslyn submodule missing; run: git submodule update --init" >&2
  exit 1
fi

if [[ "${SKIP_ROSLYN_BUILD:-0}" != 1 ]]; then
  "$roslyn/build.sh" --solution Compilers.slnf --restore --build --pack -c "$configuration" --nodeReuse false
fi

packages="$roslyn/artifacts/packages/$configuration/Shipping"
mapfile -t toolsets < <(find "$packages" -name 'Microsoft.Net.Compilers.Toolset.[0-9]*.nupkg')
if [[ ${#toolsets[@]} -ne 1 ]]; then
  echo "Expected exactly one Microsoft.Net.Compilers.Toolset package under $packages, found ${#toolsets[@]}" >&2
  exit 1
fi

version="$(unzip -p "${toolsets[0]}" Microsoft.Net.Compilers.Toolset.nuspec | sed -n 's:.*<version>\(.*\)</version>.*:\1:p')"
if [[ -z "$version" ]]; then
  echo "Could not read the package version from ${toolsets[0]}" >&2
  exit 1
fi

mkdir -p "$feed"
rm -f "$feed"/*.nupkg
for id in Microsoft.Net.Compilers.Toolset Microsoft.CodeAnalysis.Common Microsoft.CodeAnalysis.CSharp; do
  package="$(find "$packages" -name "$id.$version.nupkg" | head -n 1)"
  if [[ -z "$package" ]]; then
    echo "Missing $id.$version.nupkg under $packages" >&2
    exit 1
  fi
  cp "$package" "$feed/"
done

cat > "$repo_root/e2e/PatchedCompiler.props" <<EOF
<Project>
  <PropertyGroup>
    <PatchedCompilerVersion>$version</PatchedCompilerVersion>
    <PatchedCompilerCommit>$(git -C "$roslyn" rev-parse --short HEAD)</PatchedCompilerCommit>
  </PropertyGroup>
</Project>
EOF

echo "Patched compiler $version ($(git -C "$roslyn" rev-parse --short HEAD)) is in $feed"
