#!/usr/bin/env bash
# Compile the Unity game and editor scripts without Unity: catches compile errors in
# Assets/Game on every push, since the Unity build job needs a licence. See README.md.
set -euo pipefail
cd "$(dirname "$0")"
version=$(sed -n 's/.*<InputSystemVersion>\(.*\)<\/InputSystemVersion>.*/\1/p' Directory.Build.props)
manifest=$(sed -n 's/.*"com.unity.inputsystem": *"\([^"]*\)".*/\1/p' ../../Oduncu.Unity/Packages/manifest.json)
if [ "$version" != "$manifest" ]; then
  echo "Input System version mismatch: Directory.Build.props has $version, manifest.json has $manifest" >&2
  exit 1
fi
dir=".cache/InputSystem-$version"
if [ ! -d "$dir" ]; then
  git clone --quiet --depth 1 --branch "$version" https://github.com/Unity-Technologies/InputSystem "$dir"
fi
dotnet build Editor/Editor.csproj -c Release -nologo -v quiet "$@"
