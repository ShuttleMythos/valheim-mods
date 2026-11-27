#!/usr/bin/env bash
# Baut den Mod und packt ihn als ZIP (Thunderstore/r2modman-Format) nach dist/.
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(python3 -c "import json; print(json.load(open('package/manifest.json'))['version_number'])")
dotnet build -c Release -p:Version="$VERSION"

STAGE=$(mktemp -d)
cp package/manifest.json package/icon.png package/knochenbrecher_icon.png "$STAGE"/
cp README.md "$STAGE"/README.md
cp ../LICENSE "$STAGE"/LICENSE.txt
# Kurzanleitung für den Windows-Editor: Versionsnummer einsetzen, Windows-Zeilenenden, UTF-8 mit BOM
python3 - "$VERSION" "$STAGE/INSTALLATION.txt" <<'PY'
import sys
text = open("package/INSTALLATION.txt", encoding="utf-8").read().replace("{version}", sys.argv[1])
open(sys.argv[2], "w", encoding="utf-8-sig", newline="\r\n").write(text)
PY
cp bin/Release/net48/Knochenbrecher.dll "$STAGE"/

mkdir -p dist
rm -f "dist/Knochenbrecher-$VERSION.zip"
(cd "$STAGE" && zip -q -r "$OLDPWD/dist/Knochenbrecher-$VERSION.zip" .)
rm -rf "$STAGE"
echo "Fertig: dist/Knochenbrecher-$VERSION.zip"
